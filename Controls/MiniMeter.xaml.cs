using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace OpenCodeWidget.Controls;

public partial class MiniMeter : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(MiniMeter),
        new PropertyMetadata(0d, OnValueChanged));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent),
        typeof(Brush),
        typeof(MiniMeter),
        new PropertyMetadata(Brushes.Gold, OnAccentChanged));

    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public MiniMeter()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateFill();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public void SetData(double percent, string title, string resetText, string? tooltip = null)
    {
        Value = Math.Clamp(percent, 0, 100);
        TitleText.Text = title;
        ResetText.Text = resetText;
        PercentText.Text = FormatPercent(Value);
        ToolTip = tooltip;

        var accent = Accent ?? Brushes.Gold;
        var foreground = ResourceBrush("TextBrush");

        if (Value >= 90)
        {
            accent = ResourceBrush("DangerBrush");
            foreground = accent;
        }
        else if (Value >= 70)
        {
            accent = ResourceBrush("WarningBrush");
            foreground = accent;
        }

        Fill.Background = accent;
        PercentText.Foreground = foreground;
        UpdateFill();
    }

    public void SetEmpty(string title, string message)
    {
        Value = 0;
        TitleText.Text = title;
        ResetText.Text = message;
        PercentText.Text = "-";
        PercentText.Foreground = ResourceBrush("FaintBrush");
        Fill.Background = ResourceBrush("FaintBrush");
        ToolTip = message;
        UpdateFill();
    }

    private void UpdateFill()
    {
        var width = Track.ActualWidth;
        Fill.Width = width <= 0 ? 0 : Math.Max(0, width * Math.Clamp(Value, 0, 100) / 100);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MiniMeter meter)
            meter.UpdateFill();
    }

    private static void OnAccentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MiniMeter meter && e.NewValue is Brush brush && meter.Value < 70)
            meter.Fill.Background = brush;
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
