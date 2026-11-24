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
