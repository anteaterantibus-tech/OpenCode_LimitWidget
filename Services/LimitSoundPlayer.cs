using System.Media;

namespace OpenCodeWidget.Services;

public static class LimitSoundPlayer
{
    private static readonly Dictionary<string, Lazy<SoundPlayer?>> Players = new(StringComparer.OrdinalIgnoreCase)
    {
        ["soft"] = new(() => Create("assets/sounds/soft.wav")),
        ["bell"] = new(() => Create("assets/sounds/bell.wav")),
        ["wood"] = new(() => Create("assets/sounds/wood.wav")),
        ["drop"] = new(() => Create("assets/sounds/drop.wav")),
        ["minimal"] = new(() => Create("assets/sounds/minimal.wav")),
    };

    public static void Preload(string? id)
    {
        try
        {
            _ = Get(id).Value;
        }
        catch
        {
            // Sound is optional; ignore load failures.
        }
    }

    public static void Play(string? id)
    {
        try
        {
            Get(id).Value?.Play();
        }
        catch
        {
            // Sound is optional; ignore playback failures.
        }
    }

    private static Lazy<SoundPlayer?> Get(string? id)
    {
        if (id is not null && Players.TryGetValue(id, out var player))
            return player;

        return Players["soft"];
    }

    private static SoundPlayer? Create(string path)
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri(path, UriKind.Relative));

            if (resource?.Stream is null)
                return null;

            var player = new SoundPlayer(resource.Stream);
            player.Load();
            return player;
        }
        catch
        {
            return null;
        }
    }
}
