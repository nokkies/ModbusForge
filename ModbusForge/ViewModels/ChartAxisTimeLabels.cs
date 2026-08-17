using System;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// Total (never-throwing) time formatting for LiveCharts axis labelers.
    /// LiveCharts hands the raw axis coordinate to the labeler, and with a
    /// degenerate axis domain — for example a series whose samples all share
    /// one timestamp, or an empty hover projection — that coordinate can be
    /// NaN, ±infinity, or outside the OLE Automation date range.
    /// <see cref="DateTime.FromOADate"/> throws <see cref="ArgumentException"/>
    /// for any of those, which used to crash the UI dispatcher with a full
    /// crash dialog. A labeler must render something (even empty) for every
    /// coordinate the chart can pass.
    /// </summary>
    internal static class ChartAxisTimeLabels
    {
        // OLE Automation date range bounds: 100-01-01 .. 9999-12-31.
        private const double MinOleAutDate = -657434.0;
        private const double MaxOleAutDate = 2958465.0;

        /// <summary>
        /// Formats an OLE Automation date as "HH:mm:ss"; returns an empty
        /// string for any value <see cref="DateTime.FromOADate"/> rejects.
        /// </summary>
        public static string Time(double oadate)
        {
            if (double.IsNaN(oadate) || double.IsInfinity(oadate)) return string.Empty;
            if (oadate < MinOleAutDate || oadate > MaxOleAutDate) return string.Empty;
            return DateTime.FromOADate(oadate).ToString("HH:mm:ss");
        }
    }
}
