namespace OpenCodeWidget.Models;

public sealed class ServiceStatusResponse
{
    public long UpdatedAt { get; set; }
    public bool Stale { get; set; }
    public string? Status { get; set; }
    public List<ServiceStatusComponent> Components { get; set; } = [];
    public List<ServiceStatusIncident> Incidents { get; set; } = [];
}

public sealed class ServiceStatusIncident
{
    public string Service { get; set; } = "";
    public long StartedAt { get; set; }
    public long? ResolvedAt { get; set; }
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
    public List<ServiceStatusModel> Models { get; set; } = [];
}

public sealed class ServiceStatusComponent
{
    public string? Service { get; set; }
    public string? Status { get; set; }
    public string? Reason { get; set; }
    public long Since { get; set; }
    public List<ServiceStatusModel> Models { get; set; } = [];
}

public sealed class ServiceStatusModel
{
    public string Id { get; set; } = "";
    public string Reason { get; set; } = "";
}
