using System.Globalization;

namespace OpenCodeWidget.Services;

public static class PeakPricing
{
    private static readonly TimeSpan MorningStart = TimeSpan.FromHours(1);
    private static readonly TimeSpan MorningEnd = TimeSpan.FromHours(4);
    private static readonly TimeSpan AfternoonStart = TimeSpan.FromHours(6);
    private static readonly TimeSpan AfternoonEnd = TimeSpan.FromHours(10);

    public static bool AppliesTo(string? modelId)
    {
        return string.Equals(modelId, "deepseek-v4.1-flash", StringComparison.OrdinalIgnoreCase)
            || string.Equals(modelId, "deepseek-v4-pro", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPeak(DateTimeOffset utcNow)
    {
        if (utcNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        var time = utcNow.UtcDateTime.TimeOfDay;
        return (time >= MorningStart && time < MorningEnd)
            || (time >= AfternoonStart && time < AfternoonEnd);
    }

    public static DateTimeOffset? CurrentPeakEnd(DateTimeOffset utcNow)
    {
        if (!IsPeak(utcNow))
            return null;

        var date = utcNow.UtcDateTime.Date;
        var time = utcNow.UtcDateTime.TimeOfDay;
        var end = time < MorningEnd ? date + MorningEnd : date + AfternoonEnd;
        return new DateTimeOffset(end, TimeSpan.Zero);
    }

    // Peak windows in local time, formatted for the dollar tooltip. The UTC
    // schedule is Mon-Fri, so the local weekday may differ at the edges.
    public static (string First, string Second) LocalWindows(DateTimeOffset utcNow, CultureInfo culture)
    {
        var date = utcNow.UtcDateTime.Date;

        static string Format(DateTime day, TimeSpan from, TimeSpan to, CultureInfo culture)
        {
            var start = TimeZoneInfo.ConvertTimeFromUtc(day + from, TimeZoneInfo.Local);
            var end = TimeZoneInfo.ConvertTimeFromUtc(day + to, TimeZoneInfo.Local);
            return start.ToString("HH:mm", culture) + "-" + end.ToString("HH:mm", culture);
        }

        return (
            Format(date, MorningStart, MorningEnd, culture),
            Format(date, AfternoonStart, AfternoonEnd, culture));
    }

    public static double MinutesLeft(DateTimeOffset utcNow)
    {
        return CurrentPeakEnd(utcNow) is { } end
            ? Math.Max(0, (end - utcNow).TotalMinutes)
            : 0;
    }

    public static string CurrentPeakWindowKey(DateTimeOffset utcNow)
    {
        var startHour = utcNow.UtcDateTime.TimeOfDay < MorningEnd ? 1 : 6;
        return $"{utcNow.UtcDateTime:yyyy-MM-dd}-{startHour}";
    }
}
