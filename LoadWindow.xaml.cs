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

    private readonly LoadForecast _overall;
    private readonly LoadForecast? _model;
    private readonly string? _modelName;

    public LoadWindow(
        ServiceStatusResponse status,
        LoadForecast overall,
        LoadForecast? model,
        string? modelName)
    {
        _overall = overall;
        _model = model;
        _modelName = modelName;

        InitializeComponent();

        ApplyState(status);

        if (_model is null || string.IsNullOrWhiteSpace(_modelName))
        {
            ScopePanel.Visibility = Visibility.Collapsed;
            ShowForecast(_overall, "все модели Go", isModel: false);
        }
        else
        {
            ScopeModel.IsChecked = true;
        }
    }

    private void Scope_Changed(object sender, RoutedEventArgs e)
    {
        if (ScopeModel.IsChecked == true && _model is not null)
            ShowForecast(_model, _modelName ?? "моя модель", isModel: true);
        else
            ShowForecast(_overall, "все модели Go", isModel: false);
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

    private void ShowForecast(LoadForecast forecast, string scope, bool isModel)
    {
        ChartCaption.Text = "Нагрузка по часам · " + scope;

        BuildChart(forecast);

        SummaryText.Text = forecast.HasPattern
            ? $"Пик: {forecast.PeakRange}. Спокойнее: {forecast.QuietRange}."
            : "Выраженного суточного пика нет: нагрузка появляется в разное время.";

        var scopeNote = isModel
            ? "Оценка по инцидентам Go, где затронута эта модель"
            : "Оценка по всем инцидентам Go";

        NoteText.Text =
            $"{scopeNote} за {forecast.Days} дн. " +
            "Это не официальный прогноз, а статистика прошлых сбоев.";
    }

    private void BuildChart(LoadForecast forecast)
    {
        ChartGrid.Children.Clear();
        ChartGrid.ColumnDefinitions.Clear();

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
