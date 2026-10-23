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

            // Obsolete Block Library, Schneider Electric 33002543.19 (10/2019).
            yield return Structure("Para_PWM", // p. 343
                ("t_period", "TIME"), ("t_pause", "TIME"), ("t_brake", "TIME"), ("t_min", "TIME"), ("t_max", "TIME"),
                ("up_pos", "REAL"), ("up_neg", "REAL"));
            yield return Structure("Para_FGEN", // pp. 190-191
                ("func_no", "INT"), ("amplitude", "REAL"), ("halfperiod", "TIME"), ("t_off", "TIME"), ("t_rise", "TIME"),
                ("t_acc", "TIME"), ("unipolar", "BOOL"));

            // System Block Library, Schneider Electric 33002539.25: RRTC_DT_MS outputs (p. 247).
            yield return Structure("Display_NTPC", ("DT_value", "DT"), ("Milisecond", "INT"));
            yield return Structure("Calc_NTPC", ("Seconds", "UDINT"), ("Fraction_Second", "INT"));
        }

        private static (string, XElement) Structure(string name, params (string Field, string Type)[] fields)
            => (name, new XElement("DDTSource",
                new XAttribute("DDTName", name),
                new XElement("structure", fields.Select(f =>
                    new XElement("variables", new XAttribute("name", f.Field), new XAttribute("typeName", f.Type))))));
    }
}
