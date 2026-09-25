using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ModbusForge.Core.Plc
{
    /// <summary>The code of a user function block type, as the XEF exports it.</summary>
    public sealed class PlcDfbDefinition
    {
        public PlcDfbDefinition(string typeName, IReadOnlyList<PlcDfbSection> sections)
        {
            TypeName = typeName;
            Sections = sections;
        }

        public string TypeName { get; }

        /// <summary>The DFB's sections in execution order.</summary>
        public IReadOnlyList<PlcDfbSection> Sections { get; }
    }

    /// <summary>
    /// One section of a DFB: FBD (a detached copy of its FBDSource), ST (the text) or
    /// another language the runtime does not execute.
    /// </summary>
    public sealed record PlcDfbSection(string Name, string Language, XElement? Fbd, string? Text);

    /// <summary>The formal parameters of a function or function block, for calls that pass them by position.</summary>
    public sealed class PlcSignature
    {
        private static readonly Regex Numbered = new(@"^(?<prefix>.*?)(?<n>\d+)$", RegexOptions.Compiled);

        public PlcSignature(IReadOnlyList<string> inputs, IReadOnlyList<string> outputs, IReadOnlyList<string> inOuts, bool extensible)
        {
            Inputs = inputs;
            Outputs = outputs;
            InOuts = inOuts;
            Extensible = extensible;
        }

        public IReadOnlyList<string> Inputs { get; }
        public IReadOnlyList<string> Outputs { get; }
        public IReadOnlyList<string> InOuts { get; }

        /// <summary>More inputs can follow the last declared one (AND, ADD, MAX, MUX, ...).</summary>
        public bool Extensible { get; }

        /// <summary>
        /// The input names <paramref name="count"/> positional arguments bind to: the
        /// declared inputs in order, then for an extensible function the numbered series
        /// continued (IN1, IN2 -&gt; IN3; IN0 -&gt; IN1).
        /// </summary>
        public IReadOnlyList<string> PositionalInputs(int count)
        {
            var names = Inputs.Take(count).ToList();
            if (names.Count < count && Extensible && Inputs.Count > 0 && Numbered.Match(Inputs[^1]) is { Success: true } last)
            {
                var prefix = last.Groups["prefix"].Value;
                var n = int.Parse(last.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
                while (names.Count < count) names.Add(prefix + ++n);
            }
            return names;
        }
    }

    /// <summary>
    /// The named types of an XEF: structures and array types (DDTSource), user
    /// function blocks (FBSource, whose interface is kept even when the code is
    /// encrypted) and standard function blocks (EFBSource), plus the call signatures of
    /// every function and function block type. Types are built on first use so they
    /// can reference each other in any order.
    /// </summary>
    public sealed class PlcTypeRegistry
    {
        private readonly Dictionary<string, XElement> _ddtSources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, XElement> _fbSources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, XElement> _efbSources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PlcSignature> _signatures = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PlcType> _built = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _building = new(StringComparer.OrdinalIgnoreCase);

        // The interface sections of an FBSource/EFBSource, in the order their fields are laid out.
        private static readonly (string Section, PlcParameterDirection Direction)[] InterfaceSections =
        {
            ("inputParameters", PlcParameterDirection.Input),
            ("inOutParameters", PlcParameterDirection.InOut),
            ("outputParameters", PlcParameterDirection.Output),
            ("publicLocalVariables", PlcParameterDirection.Public),
            ("privateLocalVariables", PlcParameterDirection.Private),
        };

        public PlcTypeRegistry(XDocument xef)
        {
            foreach (var source in xef.Root?.Elements() ?? Enumerable.Empty<XElement>())
            {
                switch (source.Name.LocalName)
                {
                    case "DDTSource" when (string?)source.Attribute("DDTName") is { Length: > 0 } ddt:
                        _ddtSources.TryAdd(ddt, source);
                        break;
                    case "FBSource" when (string?)source.Attribute("nameOfFBType") is { Length: > 0 } fb:
                        _fbSources.TryAdd(fb, source);
                        _signatures.TryAdd(fb, ReadSignature(source));
                        break;
                    case "EFBSource" when (string?)source.Attribute("nameOfEFBType") is { Length: > 0 } efb:
                        _efbSources.TryAdd(efb, source);
                        _signatures.TryAdd(efb, ReadSignature(source));
                        break;
                    case "EFSource" when (string?)source.Attribute("nameOfEFType") is { Length: > 0 } ef:
                        _signatures.TryAdd(ef, ReadSignature(source));
                        break;
                }
            }
        }

        private HashSet<string>? _dfbNames;

        /// <summary>True for a user function block (DFB) type the XEF defines.</summary>
        public bool IsUserFunctionBlock(string typeName) => _dfbNames?.Contains(typeName) ?? _fbSources.ContainsKey(typeName);

        /// <summary>
        /// Builds every named type now and lets go of the XEF elements, so a compiled
        /// project does not keep the whole document in memory.
        /// </summary>
        public void Freeze()
        {
            if (_dfbNames != null) return;
            foreach (var name in _ddtSources.Keys.Concat(_fbSources.Keys).Concat(_efbSources.Keys).ToList()) ResolveNamed(name);
            _dfbNames = new HashSet<string>(_fbSources.Keys, StringComparer.OrdinalIgnoreCase);
            _ddtSources.Clear();
            _fbSources.Clear();
            _efbSources.Clear();
        }

        /// <summary>The declared parameters of a function or function block type, if the XEF has them.</summary>
        public PlcSignature? Signature(string typeName) => _signatures.TryGetValue(typeName, out var signature) ? signature : null;

        /// <summary>
        /// The type a name denotes. Undefined names (library structures such as
        /// Para_SCALING) resolve to an <see cref="PlcTypeKind.Unknown"/> type.
        /// </summary>
        public PlcType Resolve(string typeName)
        {
            var name = typeName.Trim();
            if (PlcType.TryParseBuiltIn(name, ResolveNamed) is { } builtIn) return builtIn;
            return ResolveNamed(name) ?? Remember(name, PlcType.NewUnknown(name));
        }

        private PlcType? ResolveNamed(string name)
        {
            if (_built.TryGetValue(name, out var known)) return known;

            // A self-referencing definition cannot be laid out; treat it as opaque.
            if (!_building.Add(name)) return PlcType.NewUnknown(name);
            try
            {
                if (_ddtSources.TryGetValue(name, out var ddt)) return Remember(name, BuildDdt(name, ddt));
                if (_fbSources.TryGetValue(name, out var fb)) return Remember(name, BuildFunctionBlock(name, fb, dfb: true));
                if (_efbSources.TryGetValue(name, out var efb)) return Remember(name, BuildFunctionBlock(name, efb, dfb: false));
                return null;
            }
            finally
            {
                _building.Remove(name);
            }
        }

        private PlcType Remember(string name, PlcType type)
        {
            _built[name] = type;
            return type;
        }

        private PlcType BuildDdt(string name, XElement source)
        {
            if (source.Element("array") is { } array)
            {
                var arrayType = PlcType.TryParseBuiltIn(array.Value.Trim(), ResolveNamed);
                return arrayType ?? PlcType.NewUnknown(name);
            }

            var structure = PlcType.NewStructure(name);
            foreach (var field in source.Element("structure")?.Elements("variables") ?? Enumerable.Empty<XElement>())
            {
                var fieldName = (string?)field.Attribute("name");
                var fieldType = (string?)field.Attribute("typeName");
                if (string.IsNullOrEmpty(fieldName) || string.IsNullOrEmpty(fieldType)) continue;
                structure.AddField(fieldName!, Resolve(fieldType!), PlcParameterDirection.None, Defaults(field));
            }
            return structure.Seal();
        }

        private PlcType BuildFunctionBlock(string name, XElement source, bool dfb)
        {
            var type = PlcType.NewStructure(name, PlcTypeKind.FunctionBlock);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (section, direction) in InterfaceSections)
            {
                foreach (var holder in new[] { source, source.Element("ExternalToolsOnly") })
                {
                    foreach (var variable in holder?.Element(section)?.Elements("variables") ?? Enumerable.Empty<XElement>())
                    {
                        var fieldName = (string?)variable.Attribute("name");
                        var fieldType = (string?)variable.Attribute("typeName");
                        if (string.IsNullOrEmpty(fieldName) || string.IsNullOrEmpty(fieldType) || !seen.Add(fieldName!)) continue;
                        type.AddField(fieldName!, Resolve(fieldType!), direction, Defaults(variable));
                    }
                }
            }

            type.Seal();
            if (dfb) type.Dfb = ReadCode(name, source);
            return type;
        }

        /// <summary>The DFB's sections, or null when its code is encrypted or not exported.</summary>
        private static PlcDfbDefinition? ReadCode(string name, XElement source)
        {
            if (source.Element("crypted") != null) return null;
            var sections = new List<PlcDfbSection>();
            foreach (var program in source.Elements("FBProgram"))
            {
                var sectionName = (string?)program.Attribute("name") ?? name;
                if (program.Element("FBDSource") is { } fbd)
                    sections.Add(new PlcDfbSection(sectionName, "FBD", new XElement(fbd), null));
                else if (program.Element("STSource") is { } st)
                    sections.Add(new PlcDfbSection(sectionName, "ST", null, st.Value));
                else if (program.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("Source", StringComparison.Ordinal)) is { } other)
                    sections.Add(new PlcDfbSection(sectionName, other.Name.LocalName[..^"Source".Length], null, null));
            }
            return sections.Count > 0 ? new PlcDfbDefinition(name, sections) : null;
        }

        /// <summary>A declaration's initial values: variableInit, and instanceElementDesc for its fields.</summary>
        private static IReadOnlyList<PlcInitialValue> Defaults(XElement declaration)
        {
            var values = new List<PlcInitialValue>();
            if ((string?)declaration.Element("variableInit")?.Attribute("value") is { } init)
                values.Add(new PlcInitialValue("", init));
            PlcInitialValues.Collect(declaration, "", values);
            return values;
        }

        private static PlcSignature ReadSignature(XElement source)
        {
            var holder = source.Element("ExternalToolsOnly") is { } external && external.HasElements ? external : source;
            List<string> Names(string section) => (holder.Element(section) ?? source.Element(section))?.Elements("variables")
                .Select(v => (string?)v.Attribute("name") ?? "").Where(n => n.Length > 0).ToList() ?? new List<string>();

            var inputs = Names("inputParameters");
            var extensible = inputs.RemoveAll(n => n.Equals("nin", StringComparison.OrdinalIgnoreCase)) > 0;
            return new PlcSignature(inputs, Names("outputParameters"), Names("inOutParameters"), extensible);
        }
    }

    /// <summary>Reads instanceElementDesc trees (name="field" or name="[3]" with a value, possibly nested).</summary>
    internal static class PlcInitialValues
    {
        public static void Collect(XElement parent, string prefix, List<PlcInitialValue> values)
        {
            foreach (var element in parent.Elements("instanceElementDesc"))
            {
                var name = (string?)element.Attribute("name");
                if (string.IsNullOrEmpty(name)) continue;
                var path = prefix.Length == 0 ? name! : name!.StartsWith('[') ? prefix + name : prefix + "." + name;
                if (element.Element("value") is { } value) values.Add(new PlcInitialValue(path, value.Value.Trim()));
                Collect(element, path, values);
            }
        }
    }
}
