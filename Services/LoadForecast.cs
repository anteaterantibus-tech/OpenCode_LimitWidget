using OpenCodeWidget.Models;

namespace OpenCodeWidget.Services;

public sealed class LoadForecast
{
    private const int HourCount = 24;

    public double[] HourShare { get; } = new double[HourCount];
    public int Days { get; private init; }
    public int WindowHours { get; private init; } = 6;
    public int PeakStartHour { get; private init; }
    public int QuietStartHour { get; private init; }
    public double PeakShare { get; private init; }
    public double QuietShare { get; private init; }

    public bool HasPattern => PeakShare >= 0.05;

    public string PeakRange => FormatRange(PeakStartHour, WindowHours);
    public string QuietRange => FormatRange(QuietStartHour, WindowHours);

    public int PeakEndHour => (PeakStartHour + WindowHours) % HourCount;
    public int QuietEndHour => (QuietStartHour + WindowHours) % HourCount;

    public static string FormatRange(int startHour, int hours)
    {
        var endHour = (startHour + hours) % HourCount;
        return $"{startHour:00}:00-{endHour:00}:00";
    }

    public static LoadForecast? Build(
        ServiceStatusResponse? status,
        DateTimeOffset now,
        int days = 14,
        int windowHours = 6,
        string? modelId = null)
    {
        if (status?.Incidents is not { Count: > 0 })
            return null;

        var go = status.Incidents
            .Where(i => string.Equals(i.Service, "go", StringComparison.OrdinalIgnoreCase))
            .Where(i => modelId is null || i.Models.Any(m =>
                string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (go.Length == 0)
            return null;

        var earliest = go.Min(i => DateTimeOffset.FromUnixTimeMilliseconds(i.StartedAt));
        var availableDays = Math.Max(1, Math.Min(days, (now - earliest).TotalDays));
        var windowStart = now.AddDays(-availableDays);

        var totalMinutes = new double[HourCount];
        var badMinutes = new double[HourCount];

        for (var t = windowStart; t < now; t = t.AddMinutes(1))
            totalMinutes[t.ToLocalTime().Hour]++;

        foreach (var incident in go)
        {
            var start = DateTimeOffset.FromUnixTimeMilliseconds(incident.StartedAt);
            var end = incident.ResolvedAt is { } resolvedAt
                ? DateTimeOffset.FromUnixTimeMilliseconds(resolvedAt)
                : now;

            if (start < windowStart)
                start = windowStart;
            if (end > now)
                end = now;
            if (end <= start)
                continue;

            for (var t = start; t < end; t = t.AddMinutes(1))
                badMinutes[t.ToLocalTime().Hour]++;
        }

        var hourShare = new double[HourCount];
        var anyBad = false;

        for (var hour = 0; hour < HourCount; hour++)
        {
            hourShare[hour] = totalMinutes[hour] > 0
                ? Math.Clamp(badMinutes[hour] / totalMinutes[hour], 0, 1)
                : 0;

            if (hourShare[hour] > 0)
                anyBad = true;
        }

        if (!anyBad)
            return null;

        var peakStart = 0;
        var quietStart = 0;
        var peakAverage = double.MinValue;
        var quietAverage = double.MaxValue;

        for (var start = 0; start < HourCount; start++)
        {
            var sum = 0.0;
            for (var offset = 0; offset < windowHours; offset++)
                sum += hourShare[(start + offset) % HourCount];

            var average = sum / windowHours;
            if (average > peakAverage)
            {
                peakAverage = average;
                peakStart = start;
            }

            if (average < quietAverage)
            {
                quietAverage = average;
                quietStart = start;
            }
        }

        var forecast = new LoadForecast
        {
            Days = (int)Math.Round(availableDays),
            WindowHours = windowHours,
            PeakStartHour = peakStart,
            QuietStartHour = quietStart,
            PeakShare = peakAverage,
            QuietShare = quietAverage,
        };

        for (var hour = 0; hour < HourCount; hour++)
            forecast.HourShare[hour] = hourShare[hour];

        return forecast;
    }
}
