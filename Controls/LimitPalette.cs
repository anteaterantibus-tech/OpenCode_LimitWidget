using System.Windows.Media;

namespace OpenCodeWidget.Controls;

public static class LimitPalette
{
    private readonly record struct Stop(double At, Color Color);

    // 0% used = green, 100% used = deep red. The more limit is spent,
    // the warmer the indicator becomes.
    private static readonly Stop[] Stops =
    [
        new(0.00, Color.FromRgb(0x56, 0xD2, 0x78)),
        new(0.50, Color.FromRgb(0xF5, 0xC5, 0x42)),
        new(0.75, Color.FromRgb(0xFF, 0x9F, 0x0A)),
        new(0.90, Color.FromRgb(0xFF, 0x6B, 0x6B)),
        new(1.00, Color.FromRgb(0xE5, 0x48, 0x4D)),
    ];

    public static Color ColorFor(double percentUsed)
    {
        var t = Math.Clamp(percentUsed, 0, 100) / 100.0;

        for (var i = 0; i < Stops.Length - 1; i++)
        {
            var from = Stops[i];
            var to = Stops[i + 1];

            if (t > to.At)
                continue;

            var span = to.At - from.At;
            var k = span <= 0 ? 0 : (t - from.At) / span;
            return Lerp(from.Color, to.Color, k);
        }

        return Stops[^1].Color;
    }

    public static Brush BrushFor(double percentUsed)
    {
        return new SolidColorBrush(ColorFor(percentUsed));
    }

    private static Color Lerp(Color from, Color to, double k)
    {
        k = Math.Clamp(k, 0, 1);

        return Color.FromRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * k),
            (byte)Math.Round(from.G + (to.G - from.G) * k),
            (byte)Math.Round(from.B + (to.B - from.B) * k));
    }
}
