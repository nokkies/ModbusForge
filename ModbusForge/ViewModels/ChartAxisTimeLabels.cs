using System;
using System.Globalization;
using LiveChartsCore.SkiaSharpView;

namespace ModbusForge.Avalonia.ViewModels
{
    /// <summary>
    /// Total (never-throwing) time formatting for LiveCharts axis labelers.
    ///
    /// Coordinate space: LiveCharts <c>DateTimePoint</c> exposes
    /// <c>DateTime.Ticks</c> as the X coordinate, so axis values (and the
    /// axis' <see cref="Axis.VisibleDataBounds"/>) are tick counts, not OLE
    /// Automation dates. Formatting must therefore go through the
    /// <see cref="DateTime"/> ticks constructor, never
    /// <see cref="DateTime.FromOADate"/> — the OADate range is ~100..9999 AD
    /// while real tick values are in the 10^15..10^17 range, so an OADate
    /// labeler silently renders every label as an empty string.
    ///
    /// A labeler must render something (even empty) for every coordinate the
    /// chart can pass: LiveCharts can hand the labeler NaN, ±infinity, or a
    /// value outside <see cref="DateTime"/> range (degenerate axis domain,
    /// e.g. a single-sample series, or an empty hover projection), and both
    /// the ticks constructor and <see cref="DateTime.FromOADate"/> throw for
    /// those. The crash that used to come out of the UI dispatcher for
    /// exactly this reason is why this class exists.
    /// </summary>
    internal static class ChartAxisTimeLabels
    {
        // Spans of a day or more can contain the same clock time twice (a
        // 25-hour window shows 10:00 twice), so the date is added to the
        // label at that point. Spans under a day are unambiguous on time.
        private const double SpanDaysForDateLabel = 1.0;

        private const string TimeOnlyFormat = "HH:mm:ss";
        private const string WithDateFormat = "MM-dd HH:mm";

        /// <summary>
        /// Formats an axis coordinate in <see cref="DateTime.Ticks"/> units
        /// as "HH:mm:ss"; returns an empty string for any value the
        /// <see cref="DateTime"/> ticks constructor rejects.
        /// </summary>
        public static string Time(double ticks)
        {
            return Time(ticks, (double?)null);
        }

        /// <summary>
        /// Formats an axis coordinate in tick units for an axis whose
        /// current visible span is known. The span selects the format: under
        /// a day the label is time-only; a day or more adds the month/day so
        /// repeated clock times stay distinguishable.
        /// </summary>
        public static string Time(double ticks, Axis axis)
        {
            double? spanDays = null;
            if (axis is not null)
            {
                var bounds = axis.VisibleDataBounds;
                var spanTicks = bounds.Max - bounds.Min;
                if (double.IsFinite(spanTicks) && spanTicks > 0)
                    spanDays = spanTicks / TimeSpan.TicksPerDay;
            }

            return Time(ticks, spanDays);
        }

        /// <summary>
        /// Core formatter. <paramref name="visibleSpanDays"/> is the axis'
        /// visible span in days when it is known (null otherwise); it only
        /// selects the format and never changes the guards.
        /// </summary>
        public static string Time(double ticks, double? visibleSpanDays)
        {
            // Guard the whole DateTime range, not just the positive half:
            // (long)long.MinValue rounds to -9.2e18 as a double, so a naive
            // lower bound of 0 lets long.MinValue through the cast.
            if (!double.IsFinite(ticks)) return string.Empty;
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                return string.Empty;

            var dateTime = new DateTime((long)ticks);
            var format = visibleSpanDays is >= SpanDaysForDateLabel
                ? WithDateFormat
                : TimeOnlyFormat;
            return dateTime.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
