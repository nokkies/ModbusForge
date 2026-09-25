using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using ModbusForge.Core.Plc.St;

namespace ModbusForge.Core.Plc
{
    /// <summary>
    /// Compiles an XEF into a <see cref="PlcProject"/>: the declared variables with
    /// their types and initial values, the tasks with their section order and
    /// activation conditions, each FBD section's blocks, pins and links in Control
    /// Expert's execution order, and each ST section's statements.
    /// </summary>
    public sealed class PlcProjectBuilder
    {
        private readonly PlcTypeRegistry _types;
        private readonly Dictionary<string, PlcVariable> _variables = new(StringComparer.OrdinalIgnoreCase);
        private readonly PlcGlobalScope _scope;
        private readonly List<TaskInfo> _tasks = new();
        private readonly Dictionary<string, (TaskInfo Task, int Order, string? Condition)> _assignments = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(PlcSection Section, TaskInfo Task, int Order)> _sections = new();
        private readonly List<string> _warnings = new();
        private readonly List<string> _skipped = new();

        private sealed record TaskInfo(string Name, string Kind);

        public PlcProjectBuilder(XDocument xef)
        {
            _types = new PlcTypeRegistry(xef);
            _scope = new PlcGlobalScope(_variables);
            ReadVariables(xef);
            ReadTasks(xef);
        }

        public IReadOnlyDictionary<string, PlcVariable> Variables => _variables;

        /// <summary>A section in a language the runtime does not execute (LD, IL, SFC), listed but not run.</summary>
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
            var compiler = new PlcFbdCompiler(_types, _scope, _warnings);
            var compiled = new List<PlcBlock>(blocks.Count);
            var byNodeId = new Dictionary<string, PlcBlock>(StringComparer.Ordinal);
            foreach (var (element, nodeId) in blocks)
            {
                var block = compiler.CompileBlock(element);
                block.NodeId = nodeId;
                compiled.Add(block);
                if (nodeId != null) byNodeId[nodeId] = block;
            }

            var resolved = links
                .Where(l => byNodeId.ContainsKey(l.SourceNodeId) && byNodeId.ContainsKey(l.TargetNodeId))
                .Select(l => (l.Link, byNodeId[l.SourceNodeId], byNodeId[l.TargetNodeId]));
            return Add(new PlcSectionBuilder(name, defaultTask, compiler.Connect(compiled, resolved, name), null));
        }

        /// <summary>Compiles one ST section (its program text as the XEF exports it).</summary>
        public PlcSection AddStSection(string name, string defaultTask, string source)
        {
            var program = new StCompiler(_scope, _types).Compile(source);
            foreach (var problem in program.Problems) _warnings.Add($"{name} (ST) {problem}");
            return Add(new PlcSectionBuilder(name, defaultTask, Array.Empty<PlcBlock>(), program));
        }

        private sealed record PlcSectionBuilder(string Name, string DefaultTask, IReadOnlyList<PlcBlock> Blocks, StProgram? Program);

        private PlcSection Add(PlcSectionBuilder section)
        {
            var (task, order, condition) = _assignments.TryGetValue(section.Name, out var assignment)
                ? assignment
                : (TaskFor(section.DefaultTask), int.MaxValue, null);

            var compiled = new PlcSection(section.Name, task.Name, section.Blocks,
                string.IsNullOrWhiteSpace(condition) ? null : PlcOperandParser.Parse(condition!, _scope))
            {
                Program = section.Program
            };
            _sections.Add((compiled, task, order));
            return compiled;
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

            // DFB bodies are compiled per instance while running; the types keep what that needs.
            _types.Freeze();
            var project = new PlcProject(_variables, tasks) { Types = _types };
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
                PlcInitialValues.Collect(declaration, "", variable.InitialValues);
                _variables[name!] = variable;
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
    }

    /// <summary>
    /// Compiles FBD blocks, their links and their execution order against a scope:
    /// the project's variables for sections, or one DFB instance's variables for the
    /// DFB's own FBD code.
    /// </summary>
    internal sealed class PlcFbdCompiler
    {
        private readonly PlcTypeRegistry _types;
        private readonly IPlcScope _scope;
        private readonly List<string> _warnings;

        public PlcFbdCompiler(PlcTypeRegistry types, IPlcScope scope, List<string> warnings)
        {
            _types = types;
            _scope = scope;
            _warnings = warnings;
        }

        public PlcBlock CompileBlock(XElement element)
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
                var operand = string.IsNullOrWhiteSpace(effective) ? null : PlcOperandParser.Parse(effective!, _scope);
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
            for (var i = 0; i < outputs.Count; i++) block.Call.SetOutputType(i, outputs[i].Operand?.StaticType);

            if (kind != PlcBlockKind.Function && _scope.Resolve(instanceName) is { Type.Kind: PlcTypeKind.FunctionBlock } instance)
            {
                block.InstanceRoot = instance;
            }

            var position = element.Descendants("objPosition").FirstOrDefault(p => p.Attribute("posX") != null && p.Attribute("posY") != null);
            if (position != null)
            {
                block.X = Number(position.Attribute("posX"));
                block.Y = Number(position.Attribute("posY"));
            }

            return block;
        }

        /// <summary>
        /// A whole FBDSource (a DFB's FBD code): its blocks, and its links resolved by
        /// instance name; when names repeat, the link end's recorded cell picks the block.
        /// </summary>
        public List<PlcBlock> CompileSource(XElement fbdSource, string sectionName)
        {
            var blocks = new List<PlcBlock>();
            var areas = new Dictionary<PlcBlock, (double X, double Y, double W, double H)>();
            var byName = new Dictionary<string, List<PlcBlock>>(StringComparer.Ordinal);
            foreach (var element in fbdSource.Descendants("FFBBlock"))
            {
                var block = CompileBlock(element);
                blocks.Add(block);
                areas[block] = (block.X, block.Y, Number(element.Attribute("width")), Number(element.Attribute("height")));
                if (!byName.TryGetValue(block.InstanceName, out var same)) byName[block.InstanceName] = same = new List<PlcBlock>();
                same.Add(block);
            }

            PlcBlock? End(XElement? end)
            {
                if (end == null || !byName.TryGetValue((string?)end.Attribute("parentObjectName") ?? "", out var candidates)) return null;
                if (candidates.Count == 1) return candidates[0];
                var cell = end.Element("objPosition");
                var x = Number(cell?.Attribute("posX"));
                var y = Number(cell?.Attribute("posY"));
                return candidates.FirstOrDefault(b =>
                {
                    var (bx, by, bw, bh) = areas[b];
                    return x >= bx && x <= bx + bw && y >= by && y <= by + bh;
                }) ?? candidates[^1];
            }

            var links = new List<(XElement, PlcBlock, PlcBlock)>();
            foreach (var link in fbdSource.Descendants("linkFB"))
            {
                if (End(link.Element("linkSource")) is { } source && End(link.Element("linkDestination")) is { } target)
                    links.Add((link, source, target));
                else
                    _warnings.Add($"{sectionName}: link references an unknown block");
            }

            return Connect(blocks, links, sectionName);
        }

        /// <summary>Wires the links into the input pins and returns the blocks in execution order.</summary>
        public List<PlcBlock> Connect(List<PlcBlock> blocks, IEnumerable<(XElement Link, PlcBlock Source, PlcBlock Target)> links, string sectionName)
        {
            var edges = new List<(PlcBlock From, PlcBlock To)>();
            foreach (var (link, source, target) in links)
            {
                var sourcePin = (string?)link.Element("linkSource")?.Attribute("pinName") ?? "";
                var targetPin = (string?)link.Element("linkDestination")?.Attribute("pinName") ?? "";

                var input = FindPin(target.Inputs, targetPin)
                            ?? (string.Equals(targetPin, "EN", StringComparison.OrdinalIgnoreCase) ? target.En : null);
                if (input == null)
                {
                    _warnings.Add($"{sectionName}: link to unknown pin {target.InstanceName}.{targetPin}");
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
                        _warnings.Add($"{sectionName}: link from unknown pin {source.InstanceName}.{sourcePin}");
                        continue;
                    }
                }

                input.SourceBlock = source;
                input.SourceOutput = outputIndex;
                edges.Add((source, target));
            }

            return ExecutionOrder(blocks, edges, sectionName);
        }

        private static double Number(XAttribute? attribute)
            => double.TryParse((string?)attribute, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

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
