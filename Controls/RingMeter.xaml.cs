using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;
using UserControl = System.Windows.Controls.UserControl;

namespace OpenCodeWidget.Controls;

public partial class RingMeter : UserControl
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly Duration AnimationDuration = new(TimeSpan.FromMilliseconds(520));

    public RingMeter()
    {
        InitializeComponent();
    }

    public void SetData(double percent, string title, string resetText, string? tooltip = null)
    {
        percent = Math.Clamp(percent, 0, 100);

        TitleText.Text = title;
        ResetText.Text = resetText;
        PercentText.Text = FormatPercent(percent);
        ToolTip = tooltip;

        var accent = LimitPalette.BrushFor(percent);
        PercentText.Foreground = accent;
        Ring.Accent = accent;

        var animation = new DoubleAnimation(percent, AnimationDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Ring.BeginAnimation(RingRenderer.ValueProperty, animation);
    }

    public void SetEmpty(string title, string message)
    {
        TitleText.Text = title;
        ResetText.Text = message;
        PercentText.Text = "-";
        PercentText.Foreground = ResourceBrush("FaintBrush");
        Ring.Accent = ResourceBrush("FaintBrush");
        ToolTip = message;
        Ring.BeginAnimation(RingRenderer.ValueProperty, null);
        Ring.Value = 0;
    }

    private static Brush ResourceBrush(string key)
    {
        return System.Windows.Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    private static string FormatPercent(double percent)
    {
        if (percent > 0 && percent < 1)
            return "<1%";
        if (percent < 10)
            return percent.ToString("0.0", Ru) + "%";
        return Math.Round(percent).ToString("0", Ru) + "%";
    }
}
