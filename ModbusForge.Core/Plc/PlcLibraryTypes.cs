using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace ModbusForge.Core.Plc
{
    /// <summary>
    /// Structures the Control Expert libraries define themselves, such as the PARA
    /// parameter of SCALING. An XEF uses them without exporting their definition, so
    /// the runtime supplies them, in the DDTSource form an XEF would carry, and lays
    /// them out like any other structure. An XEF that does define one wins.
    /// </summary>
    internal static class PlcLibraryTypes
    {
        public static IEnumerable<(string Name, XElement Source)> Structures()
        {
            // Control Block Library, Schneider Electric 33002535.16 (09/2020).
            yield return Structure("Para_SCALING", // p. 93
                ("in_min", "REAL"), ("in_max", "REAL"), ("out_min", "REAL"), ("out_max", "REAL"), ("clip", "BOOL"));
            yield return Structure("Para_RAMP", // p. 331
                ("inc_rate", "REAL"), ("dec_rate", "REAL"));
        }

        private static (string, XElement) Structure(string name, params (string Field, string Type)[] fields)
            => (name, new XElement("DDTSource",
                new XAttribute("DDTName", name),
                new XElement("structure", fields.Select(f =>
                    new XElement("variables", new XAttribute("name", f.Field), new XAttribute("typeName", f.Type))))));
    }
}
