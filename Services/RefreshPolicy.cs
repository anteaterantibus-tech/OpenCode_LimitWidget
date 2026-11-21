namespace OpenCodeWidget.Services;

public enum LinkState
{
    Ok,
    Degraded,
    Down,
}

public sealed class RefreshPolicy
{
    private static readonly Random Jitter = new();
    private static readonly TimeSpan LogInterval = TimeSpan.FromMinutes(5);

    public int ConsecutiveFailures { get; private set; }
    public GoApiErrorKind? LastKind { get; private set; }
    public DateTimeOffset? FirstFailureAt { get; private set; }
    public DateTimeOffset? LastSuccessAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? LastLogAt { get; private set; }
    public bool BannerVisible { get; private set; }
    public bool ShouldLog { get; private set; }

    public int RestoredAfterFailures { get; private set; }
    public TimeSpan? RestoredAfter { get; private set; }
    public bool Restored { get; private set; }

    public LinkState State =>
        ConsecutiveFailures == 0
            ? LinkState.Ok
            : LastKind == GoApiErrorKind.Network && ConsecutiveFailures < 3
                ? LinkState.Degraded
                : LinkState.Down;

    public void SeedFromCache(DateTimeOffset? fetchedAt)
    {
        LastSuccessAt = fetchedAt;
    }

    public void Reset()
    {
        ConsecutiveFailures = 0;
        LastKind = null;
        FirstFailureAt = null;
        BannerVisible = false;
        ShouldLog = false;
        Restored = false;
        RestoredAfterFailures = 0;
        RestoredAfter = null;
    }

    public void OnSuccess(DateTimeOffset now)
    {
        RestoredAfterFailures = ConsecutiveFailures;
        RestoredAfter = FirstFailureAt is { } first ? now - first : null;
        Restored = ConsecutiveFailures > 0;

        ConsecutiveFailures = 0;
        LastKind = null;
        FirstFailureAt = null;
        LastSuccessAt = now;
        BannerVisible = false;
        ShouldLog = false;
    }

    public TimeSpan OnFailure(GoApiErrorKind kind, TimeSpan normalInterval, DateTimeOffset now)
    {
        LastAttemptAt = now;
        LastKind = kind;
        ConsecutiveFailures++;
        FirstFailureAt ??= now;

        var logDue = LastLogAt is null || now - LastLogAt >= LogInterval;
        ShouldLog = kind == GoApiErrorKind.Network
            ? ConsecutiveFailures == 1 || logDue
            : logDue;

        if (ShouldLog)
            LastLogAt = now;

        var staleAfter = TimeSpan.FromSeconds(Math.Max(90, normalInterval.TotalSeconds * 2));
        var stale = LastSuccessAt is null || now - LastSuccessAt > staleAfter;

        BannerVisible =
            kind is GoApiErrorKind.Unauthorized or GoApiErrorKind.RateLimited or GoApiErrorKind.Server
            || (kind == GoApiErrorKind.Network && (ConsecutiveFailures >= 3 || stale));

        return kind switch
        {
            GoApiErrorKind.Network => Backoff(ConsecutiveFailures),
            GoApiErrorKind.Server => TimeSpan.FromSeconds(15),
            GoApiErrorKind.RateLimited => TimeSpan.FromSeconds(60),
            _ => normalInterval,
        };
    }

    private static TimeSpan Backoff(int failures)
    {
        var seconds = 15 * Math.Pow(2, Math.Min(failures - 1, 4));
        var jitter = 0.85 + Jitter.NextDouble() * 0.3;
        return TimeSpan.FromSeconds(Math.Clamp(seconds * jitter, 10, 300));
    }
}
