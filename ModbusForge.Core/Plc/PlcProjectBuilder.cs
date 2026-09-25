using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace ModbusForge.Core.Plc
{
    /// <summary>
    /// Compiles an XEF into a <see cref="PlcProject"/>: the declared variables with
    /// their types and initial values, the tasks with their section order and
    /// activation conditions, and each FBD section's blocks, pins and links in
    /// Control Expert's execution order.
    /// </summary>
    public sealed class PlcProjectBuilder
    {
        private readonly PlcTypeRegistry _types;
        private readonly Dictionary<string, PlcVariable> _variables = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<TaskInfo> _tasks = new();
        private readonly Dictionary<string, (TaskInfo Task, int Order, string? Condition)> _assignments = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(PlcSection Section, TaskInfo Task, int Order)> _sections = new();
        private readonly List<string> _warnings = new();
        private readonly List<string> _skipped = new();

        private sealed record TaskInfo(string Name, string Kind);

        public PlcProjectBuilder(XDocument xef)
        {
            _types = new PlcTypeRegistry(xef);
            ReadVariables(xef);
            ReadTasks(xef);
        }

        public IReadOnlyDictionary<string, PlcVariable> Variables => _variables;

        /// <summary>A section in another language, listed but not executed.</summary>
        public void AddSkippedSection(string name, string language) => _skipped.Add($"{name} ({language})");

        /// <summary>
        /// Compiles one FBD section. <paramref name="blocks"/> pairs every FFBBlock with
        /// the editor node id it is drawn as; <paramref name="links"/> pairs every linkFB
        /// with the node ids its ends were resolved to.
        /// </summary>
        public PlcSection AddSection(
            string name,
            string defaultTask,
            IReadOnlyList<(XElement Block, string? NodeId)> blocks,
            IReadOnlyList<(XElement Link, string SourceNodeId, string TargetNodeId)> links)
        {
            var compiled = new List<PlcBlock>(blocks.Count);
            var byNodeId = new Dictionary<string, PlcBlock>(StringComparer.Ordinal);
            foreach (var (element, nodeId) in blocks)
            {
                var block = CompileBlock(element);
                block.NodeId = nodeId;
                compiled.Add(block);
                if (nodeId != null) byNodeId[nodeId] = block;
            }

            var edges = new List<(PlcBlock From, PlcBlock To)>();
            foreach (var (link, sourceId, targetId) in links)
            {
                if (!byNodeId.TryGetValue(sourceId, out var source) || !byNodeId.TryGetValue(targetId, out var target)) continue;
                var sourcePin = (string?)link.Element("linkSource")?.Attribute("pinName") ?? "";
                var targetPin = (string?)link.Element("linkDestination")?.Attribute("pinName") ?? "";

                var input = FindPin(target.Inputs, targetPin)
                            ?? (string.Equals(targetPin, "EN", StringComparison.OrdinalIgnoreCase) ? target.En : null);
                if (input == null)
                {
                    _warnings.Add($"{name}: link to unknown pin {target.InstanceName}.{targetPin}");
                    continue;
                }

                int outputIndex;
                if (string.Equals(sourcePin, "ENO", StringComparison.OrdinalIgnoreCase))
                {
                    outputIndex = -1;
                }
                else
                {
                    outputIndex = IndexOfPin(source.Outputs, sourcePin);
                    if (outputIndex < 0)
                    {
                        _warnings.Add($"{name}: link from unknown pin {source.InstanceName}.{sourcePin}");
                        continue;
                    }
                }

                input.SourceBlock = source;
                input.SourceOutput = outputIndex;
                edges.Add((source, target));
            }

            var ordered = ExecutionOrder(compiled, edges, name);
            var (task, order, condition) = _assignments.TryGetValue(name, out var assignment)
                ? assignment
                : (TaskFor(defaultTask), int.MaxValue, null);

            var section = new PlcSection(name, task.Name, ordered,
                string.IsNullOrWhiteSpace(condition) ? null : PlcOperandParser.Parse(condition!, _variables));
            _sections.Add((section, task, order));
            return section;
        }

        public PlcProject Build()
        {
            // FAST runs at a higher priority than MAST; auxiliary tasks after.
            static int Priority(string task) => task.StartsWith("FAST", StringComparison.OrdinalIgnoreCase) ? 0
                : task.Equals("MAST", StringComparison.OrdinalIgnoreCase) ? 1 : 2;

            var tasks = _sections
                .GroupBy(s => s.Task.Name, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => Priority(g.Key))
                .Select(g => new PlcTask(
                    g.Key,
                    g.First().Task.Kind,
                    g.OrderBy(s => s.Order).Select(s => s.Section).ToList()))
                .ToList();

            var project = new PlcProject(_variables, tasks);
            project.Warnings.AddRange(_warnings);
            project.SkippedSections.AddRange(_skipped);
            return project;
        }

        // ------------------------------------------------------------------
        // Variables and tasks
        // ------------------------------------------------------------------

        private void ReadVariables(XDocument xef)
        {
            foreach (var declaration in xef.Descendants("dataBlock").FirstOrDefault()?.Elements("variables") ?? Enumerable.Empty<XElement>())
            {
                var name = (string?)declaration.Attribute("name");
                var typeName = (string?)declaration.Attribute("typeName");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(typeName) || _variables.ContainsKey(name!)) continue;

                var addressText = (string?)declaration.Attribute("topologicalAddress");
                var address = string.IsNullOrWhiteSpace(addressText) ? null : PlcAddress.TryParse(addressText!);
                if (!string.IsNullOrWhiteSpace(addressText) && address == null)
                    _warnings.Add($"{name}: unsupported address {addressText}");

                var variable = new PlcVariable(name!, _types.Resolve(typeName!), address);
                if ((string?)declaration.Element("variableInit")?.Attribute("value") is { } init)
                    variable.InitialValues.Add(new PlcInitialValue("", init));
                CollectInitialValues(declaration, "", variable.InitialValues);
                _variables[name!] = variable;
            }
        }

        /// <summary>instanceElementDesc name="field"|"[3]" with a value, possibly nested.</summary>
        private static void CollectInitialValues(XElement parent, string prefix, List<PlcInitialValue> values)
        {
            foreach (var element in parent.Elements("instanceElementDesc"))
            {
                var name = (string?)element.Attribute("name");
                if (string.IsNullOrEmpty(name)) continue;
                var path = prefix.Length == 0 ? name! : name!.StartsWith('[') ? prefix + name : prefix + "." + name;
                if (element.Element("value") is { } value) values.Add(new PlcInitialValue(path, value.Value.Trim()));
                CollectInitialValues(element, path, values);
            }
        }

        private void ReadTasks(XDocument xef)
        {
            foreach (var taskDesc in xef.Descendants("logicConf").FirstOrDefault()?.Descendants("taskDesc") ?? Enumerable.Empty<XElement>())
            {
                var task = new TaskInfo((string?)taskDesc.Attribute("task") ?? "MAST", (string?)taskDesc.Attribute("taskType") ?? "cyclic");
                _tasks.Add(task);
                var order = 0;
                foreach (var sectionDesc in taskDesc.Elements("sectionDesc"))
                {
                    var name = (string?)sectionDesc.Attribute("name");
                    if (string.IsNullOrEmpty(name)) continue;
                    _assignments[name!] = (task, order++, (string?)sectionDesc.Attribute("activationCondition"));
                }
            }
        }

        private TaskInfo TaskFor(string name)
            => _tasks.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? new TaskInfo(string.IsNullOrWhiteSpace(name) ? "MAST" : name, "cyclic");

        // ------------------------------------------------------------------
        // Blocks
        // ------------------------------------------------------------------

        private PlcBlock CompileBlock(XElement element)
        {
            var rawType = (string?)element.Attribute("typeName") ?? "";
            var dot = rawType.LastIndexOf('.');
            var typeName = dot >= 0 ? rawType[(dot + 1)..] : rawType;
            var instanceName = (string?)element.Attribute("instanceName") ?? "";

            PlcBlockKind kind;
            PlcBlockBehavior? behavior = null;
            if (_types.IsUserFunctionBlock(typeName))
            {
                kind = PlcBlockKind.UserFunctionBlock;
            }
            else
            {
                behavior = PlcStandardLibrary.Find(typeName);
                kind = behavior == null ? PlcBlockKind.Unsupported
                    : behavior.IsFunctionBlock ? PlcBlockKind.FunctionBlock : PlcBlockKind.Function;
            }

            var inputs = new List<PlcBlockPin>();
            var outputs = new List<PlcBlockPin>();
            PlcBlockPin? en = null, eno = null;
            var description = element.Element("descriptionFFB");
            foreach (var variable in description?.Elements() ?? Enumerable.Empty<XElement>())
            {
                var isInput = variable.Name.LocalName == "inputVariable";
                if (!isInput && variable.Name.LocalName != "outputVariable") continue;

                var formal = (string?)variable.Attribute("formalParameter") ?? "";
                var effective = (string?)variable.Attribute("effectiveParameter");
                var inverted = string.Equals((string?)variable.Attribute("invertedPin"), "true", StringComparison.OrdinalIgnoreCase);
                var operand = string.IsNullOrWhiteSpace(effective) ? null : PlcOperandParser.Parse(effective!, _variables);
                var pin = new PlcBlockPin(formal, inverted, operand);

                if (isInput && formal.Equals("EN", StringComparison.OrdinalIgnoreCase)) en = pin;
                else if (!isInput && formal.Equals("ENO", StringComparison.OrdinalIgnoreCase)) eno = pin;
                else (isInput ? inputs : outputs).Add(pin);
            }

            var block = new PlcBlock(typeName, instanceName, kind, behavior, inputs, outputs, en, eno)
            {
                ExecuteAfter = string.IsNullOrWhiteSpace((string?)description?.Attribute("execAfter"))
                    ? null
                    : ((string)description!.Attribute("execAfter")!).Trim()
            };

            if (kind != PlcBlockKind.Function && _variables.TryGetValue(instanceName, out var instance)
                && instance.Type.Kind is PlcTypeKind.FunctionBlock)
            {
                block.Instance = instance;
            }

            var position = element.Descendants("objPosition").FirstOrDefault(p => p.Attribute("posX") != null && p.Attribute("posY") != null);
            if (position != null)
            {
                block.X = double.TryParse((string?)position.Attribute("posX"), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : 0;
                block.Y = double.TryParse((string?)position.Attribute("posY"), NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ? y : 0;
            }

            return block;
        }

        private static PlcBlockPin? FindPin(IReadOnlyList<PlcBlockPin> pins, string name)
        {
            var index = IndexOfPin(pins, name);
            return index >= 0 ? pins[index] : null;
        }

        private static int IndexOfPin(IReadOnlyList<PlcBlockPin> pins, string name)
        {
            for (var i = 0; i < pins.Count; i++)
            {
                if (string.Equals(pins[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        /// <summary>
        /// Control Expert's FBD execution order (Program Languages and Structure,
        /// 35006144, "Execution Sequence of the FFBs"; FAQ FA332812): links decide
        /// first (a block runs after every block feeding it), then an explicit
        /// "Execute after", then position: networks (blocks joined by links) are
        /// processed one after another from the top, and blocks the links leave free
        /// run top to bottom, then left to right.
        /// </summary>
        private List<PlcBlock> ExecutionOrder(List<PlcBlock> blocks, List<(PlcBlock From, PlcBlock To)> edges, string sectionName)
        {
            // Networks: blocks connected by links.
            var parent = blocks.ToDictionary(b => b, b => b);
            PlcBlock Find(PlcBlock b)
            {
                while (!ReferenceEquals(parent[b], b)) b = parent[b] = parent[parent[b]];
                return b;
            }
            foreach (var (from, to) in edges) parent[Find(from)] = Find(to);

            // A network sits where its top-most (then left-most) block sits.
            var networkRank = blocks
                .GroupBy(Find)
                .Select(g => (Root: g.Key, Top: g.OrderBy(b => b.Y).ThenBy(b => b.X).First()))
                .OrderBy(n => n.Top.Y)
                .ThenBy(n => n.Top.X)
                .Select((n, rank) => (n.Root, rank))
                .ToDictionary(p => p.Root, p => p.rank);
            var declared = blocks.Select((b, i) => (b, i)).ToDictionary(p => p.b, p => p.i);

            // Dependencies: links, plus "Execute after" by instance name.
            var successors = blocks.ToDictionary(b => b, _ => new List<PlcBlock>());
            var pending = blocks.ToDictionary(b => b, _ => 0);
            void AddEdge(PlcBlock from, PlcBlock to)
            {
                if (ReferenceEquals(from, to)) return;
                successors[from].Add(to);
                pending[to]++;
            }
            foreach (var (from, to) in edges) AddEdge(from, to);

            var byInstance = blocks.GroupBy(b => b.InstanceName, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (var block in blocks)
            {
                if (block.ExecuteAfter != null && byInstance.TryGetValue(block.ExecuteAfter, out var before)) AddEdge(before, block);
            }

            var ready = new SortedSet<PlcBlock>(Comparer<PlcBlock>.Create((a, b) =>
            {
                var c = networkRank[Find(a)].CompareTo(networkRank[Find(b)]);
                if (c == 0) c = a.Y.CompareTo(b.Y);
                if (c == 0) c = a.X.CompareTo(b.X);
                if (c == 0) c = declared[a].CompareTo(declared[b]);
                return c;
            }));
            foreach (var block in blocks.Where(b => pending[b] == 0)) ready.Add(block);

            var order = new List<PlcBlock>(blocks.Count);
            while (ready.Count > 0)
            {
                var next = ready.Min!;
                ready.Remove(next);
                order.Add(next);
                foreach (var successor in successors[next])
                {
                    if (--pending[successor] == 0) ready.Add(successor);
                }
            }

            if (order.Count < blocks.Count)
            {
                // Links forming a loop (Control Expert rejects these): run the rest by position.
                var done = new HashSet<PlcBlock>(order);
                var rest = blocks.Where(b => !done.Contains(b)).OrderBy(b => b.Y).ThenBy(b => b.X).ToList();
                _warnings.Add($"{sectionName}: {rest.Count} block(s) in a link loop run in position order");
                order.AddRange(rest);
            }

            return order;
        }
    }
}
