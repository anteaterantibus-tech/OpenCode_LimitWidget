using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;
using UserControl = System.Windows.Controls.UserControl;

namespace OpenCodeWidget.Controls;

public partial class RingMeter : UserControl
{
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent),
        typeof(Brush),
        typeof(RingMeter),
        new PropertyMetadata(Brushes.Gold, OnAccentChanged));

    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly Duration AnimationDuration = new(TimeSpan.FromMilliseconds(520));

    public RingMeter()
    {
        InitializeComponent();
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public void SetData(double percent, string title, string resetText, string? tooltip = null)
    {
        percent = Math.Clamp(percent, 0, 100);

        TitleText.Text = title;
        ResetText.Text = resetText;
        PercentText.Text = FormatPercent(percent);
        ToolTip = tooltip;

        var foreground = ResourceBrush("TextBrush");
        var accent = Accent ?? Brushes.Gold;

        if (percent >= 90)
        {
            accent = ResourceBrush("DangerBrush");
            foreground = accent;
        }
        else if (percent >= 70)
        {
            accent = ResourceBrush("WarningBrush");
            foreground = accent;
        }

        PercentText.Foreground = foreground;
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

    private static void OnAccentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RingMeter meter && meter.Ring is not null && e.NewValue is Brush brush)
            meter.Ring.Accent = brush;
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
