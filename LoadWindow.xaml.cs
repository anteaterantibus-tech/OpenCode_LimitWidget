using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenCodeWidget.Models;
using OpenCodeWidget.Services;

namespace OpenCodeWidget;

public partial class LoadWindow : Window
{
    private const double ChartHeight = 92;

    public LoadWindow(ServiceStatusResponse status, LoadForecast forecast)
    {
        InitializeComponent();

        ApplyState(status);
        BuildChart(forecast);
        ApplyForecast(forecast);
    }

    private void ApplyState(ServiceStatusResponse status)
    {
        var go = status.Components.FirstOrDefault(c =>
            string.Equals(c.Service, "go", StringComparison.OrdinalIgnoreCase));

        var state = go?.Status ?? status.Status ?? "operational";
        var level = state.Trim().ToLowerInvariant();

        var brush = (Brush)FindResource(level switch
        {
            "outage" => "DangerBrush",
            "degraded" => "WarningBrush",
            _ => "SuccessBrush",
        });

        StateIcon.Foreground = brush;
        StateText.Foreground = brush;

        StateText.Text = level switch
        {
            "outage" => "Сбой",
            "degraded" => "Повышенная нагрузка",
            _ => "Нагрузка в норме",
        };

        var detail = level switch
        {
            "outage" => "Сервис Go сообщает о сбое.",
            "degraded" => "Сервис Go работает с ограничениями.",
            _ => "Сервис Go работает штатно.",
        };

        var reason = ReasonText(go?.Reason);
        if (reason.Length > 0)
            detail += " Причина: " + reason + ".";

        if (go?.Models is { Count: > 0 } models)
        {
            var names = models
                .Select(m => MainWindow.FriendlyModel(m.Id))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            detail += " Модели: " + string.Join(", ", names) + ".";
        }

        StateDetail.Text = detail;
    }

    private void BuildChart(LoadForecast forecast)
    {
        for (var hour = 0; hour < 24; hour++)
            ChartGrid.ColumnDefinitions.Add(new ColumnDefinition());

        var currentHour = DateTimeOffset.Now.Hour;
        var warning = (Brush)FindResource("WarningBrush");
        var text = (Brush)FindResource("TextBrush");

        for (var hour = 0; hour < 24; hour++)
        {
            var share = forecast.HourShare[hour];
            var isCurrent = hour == currentHour;

            var bar = new Border
            {
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = Math.Max(2, share * (ChartHeight - 4)),
                Margin = new Thickness(1.5, 0, 1.5, 0),
                Background = isCurrent ? text : warning,
                Opacity = isCurrent ? 0.95 : 0.5,
                ToolTip = $"{hour:00}:00 · {share * 100:0}% времени с нагрузкой",
            };

            var host = new Grid();
            host.Children.Add(bar);
            ChartGrid.Children.Add(host);
            Grid.SetColumn(host, hour);
        }
    }

    private void ApplyForecast(LoadForecast forecast)
    {
        SummaryText.Text = forecast.HasPattern
            ? $"Пик: {forecast.PeakRange}. Спокойнее: {forecast.QuietRange}."
            : "Выраженного суточного пика нет: нагрузка появляется в разное время.";

        NoteText.Text =
            $"Оценка по истории status.opencode.ai за {forecast.Days} дн. " +
            "Это не официальный прогноз, а статистика прошлых сбоев.";
    }

    private static string ReasonText(string? reason)
    {
        return reason?.Trim().ToLowerInvariant() switch
        {
            "upstream" => "проблема на стороне провайдера",
            "latency" => "повышенные задержки",
            "errors" => "ошибки запросов",
            "availability" => "недоступность",
            _ => "",
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void StatusLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://status.opencode.ai")
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // No browser available; ignore.
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
