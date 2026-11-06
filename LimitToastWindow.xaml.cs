using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OpenCodeWidget;

public partial class LimitToastWindow : Window
{
    private readonly DispatcherTimer _timer;
    private int _generation;

    public LimitToastWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _timer.Tick += (_, _) => CloseToast();
    }

    public void ShowMessage(string text, int level, Rect mainWindowRect, double fallbackWidth)
    {
        _generation++;

        MessageText.Text = text;

        var brush = (Brush)FindResource(level >= 90 ? "DangerBrush" : "WarningBrush");
        Card.BorderBrush = brush;
        ProgressFill.Background = brush;

        Position(mainWindowRect, fallbackWidth);

        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));

        _timer.Stop();
        _timer.Start();

        ProgressScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromSeconds(8))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn },
            });
    }

    public void HideToast()
    {
        _timer.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Hide();
    }

    private void Position(Rect mainWindowRect, double fallbackWidth)
    {
        var work = SystemParameters.WorkArea;

        double left;
        double top;

        if (mainWindowRect.IsEmpty)
        {
            Width = Math.Max(280, fallbackWidth - 8);
            left = work.Right - Width - 24;
            top = work.Bottom - Height - 24;
        }
        else
        {
            Width = Math.Max(280, mainWindowRect.Width - 8);
            left = mainWindowRect.Right - Width - 4;
            top = mainWindowRect.Bottom - 20;

            if (top + Height > work.Bottom - 8)
                top = mainWindowRect.Top + 20 - Height;
        }

        left = Math.Max(work.Left + 8, Math.Min(left, work.Right - Width - 8));
        top = Math.Max(work.Top + 8, Math.Min(top, work.Bottom - Height - 8));

        Left = left;
        Top = top;
    }

    private void CloseToast()
    {
        _timer.Stop();
        var generation = ++_generation;

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) =>
        {
            if (generation != _generation)
                return;

            Opacity = 0;
            Hide();
        };

        BeginAnimation(OpacityProperty, fade);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        CloseToast();
    }
}
