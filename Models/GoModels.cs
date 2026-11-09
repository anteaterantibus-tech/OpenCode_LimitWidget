using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCodeWidget.Models;

public sealed class GoStatusResponse
{
    public string? SubscriberUserId { get; set; }
    public string? Product { get; set; }
    public string? RenewalProduct { get; set; }
    public bool UseBalance { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public bool RenewalPending { get; set; }
    public GoAccess? Access { get; set; }
}

public sealed class GoAccess
{
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public GoMeters? Meters { get; set; }
}

public sealed class GoMeters
{
    public GoMeter? FiveHour { get; set; }
    public GoMeter? Week { get; set; }
    public GoMeter? Month { get; set; }
}

public sealed class GoMeter
{
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? ResetsAt { get; set; }
    public long LimitMicroCents { get; set; }
    public long UsedMicroCents { get; set; }

    public double Percent =>
        LimitMicroCents <= 0 ? 0 : Math.Clamp((double)UsedMicroCents / LimitMicroCents * 100, 0, 100);

    public decimal UsedUsd => UsedMicroCents / 100_000_000m;

    public decimal LimitUsd => LimitMicroCents / 100_000_000m;
}

public sealed class GoActivityResponse
{
    public DateTimeOffset StartsAt { get; set; }
    public List<GoActivityDay> Daily { get; set; } = [];
    public List<GoActivityModel> Models { get; set; } = [];
}

public sealed class GoActivityDay
{
    public string Date { get; set; } = "";
    public long TotalCostMicroCents { get; set; }
    public long TotalTokens { get; set; }
    public long TotalRequests { get; set; }
}

public sealed class GoActivityModel
{
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public long TotalRequests { get; set; }
}

public sealed class GoSnapshot
{
    public GoStatusResponse? Status { get; set; }
    public GoActivityResponse? Activity { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}

public static class GoJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
