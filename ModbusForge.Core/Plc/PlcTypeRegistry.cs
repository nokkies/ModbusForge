using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace ModbusForge.Core.Plc
{
    /// <summary>
    /// The named types of an XEF: structures and array types (DDTSource), user
    /// function blocks (FBSource, whose interface is kept even when the code is
    /// encrypted) and standard function blocks (EFBSource). Types are built on first
    /// use so they can reference each other in any order.
    /// </summary>
    public sealed class PlcTypeRegistry
    {
        private readonly Dictionary<string, XElement> _ddtSources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, XElement> _fbSources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, XElement> _efbSources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PlcType> _built = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _building = new(StringComparer.OrdinalIgnoreCase);

        // The interface sections of an FBSource/EFBSource, in the order their fields are laid out.
        private static readonly string[] InterfaceSections =
            { "inputParameters", "inOutParameters", "outputParameters", "publicLocalVariables", "privateLocalVariables" };

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
                        break;
                    case "EFBSource" when (string?)source.Attribute("nameOfEFBType") is { Length: > 0 } efb:
                        _efbSources.TryAdd(efb, source);
                        break;
                }
            }
        }

        /// <summary>True for a user function block (DFB) type the XEF defines.</summary>
        public bool IsUserFunctionBlock(string typeName) => _fbSources.ContainsKey(typeName);

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
                if (_fbSources.TryGetValue(name, out var fb)) return Remember(name, BuildFunctionBlock(name, fb));
                if (_efbSources.TryGetValue(name, out var efb)) return Remember(name, BuildFunctionBlock(name, efb));
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
                structure.AddField(fieldName!, Resolve(fieldType!));
            }
            return structure.Seal();
        }

        private PlcType BuildFunctionBlock(string name, XElement source)
        {
            var type = PlcType.NewStructure(name, PlcTypeKind.FunctionBlock);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in InterfaceSections)
            {
                foreach (var holder in new[] { source, source.Element("ExternalToolsOnly") })
                {
                    foreach (var variable in holder?.Element(section)?.Elements("variables") ?? Enumerable.Empty<XElement>())
                    {
                        var fieldName = (string?)variable.Attribute("name");
                        var fieldType = (string?)variable.Attribute("typeName");
                        if (string.IsNullOrEmpty(fieldName) || string.IsNullOrEmpty(fieldType) || !seen.Add(fieldName!)) continue;
                        type.AddField(fieldName!, Resolve(fieldType!));
                    }
                }
            }
            return type.Seal();
        }
    }
}
