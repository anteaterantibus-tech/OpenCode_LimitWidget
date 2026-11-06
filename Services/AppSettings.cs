namespace OpenCodeWidget.Services;

public sealed class AppSettings
{
    public string? ProtectedApiKey { get; set; }
    public int RefreshSeconds { get; set; } = 60;
    public bool AlwaysOnTop { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool CompactMode { get; set; }
    public bool TaskbarMode { get; set; }
    public bool HideInFullscreen { get; set; } = true;
    public bool NotifyOnLimits { get; set; } = true;
    public bool NotifySound { get; set; } = true;
    public string NotifySoundId { get; set; } = "soft";
    public bool AnimateOnLimit { get; set; } = true;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
