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
    private readonly ServiceStatusResponse _status;

    private LoadForecast _currentForecast;
    private bool _currentIsModel;

    public LoadWindow(
        ServiceStatusResponse status,
        LoadForecast overall,
        LoadForecast? model,
        string? modelName)
    {
        _status = status;
        _overall = overall;
        _model = model;
        _modelName = modelName;
        _currentForecast = overall;

        InitializeComponent();

        Loc.Changed += OnLanguageChanged;
        Closed += (_, _) => Loc.Changed -= OnLanguageChanged;

        ApplyState(_status);

        if (_model is null || string.IsNullOrWhiteSpace(_modelName))
        {
            ScopePanel.Visibility = Visibility.Collapsed;
            ShowForecast(_overall, isModel: false);
        }
        else
        {
            ScopeModel.IsChecked = true;
        }
    }

    private void OnLanguageChanged()
    {
        ApplyState(_status);
        ShowForecast(_currentForecast, _currentIsModel);
    }

    private void Scope_Changed(object sender, RoutedEventArgs e)
    {
        if (ScopeModel.IsChecked == true && _model is not null)
            ShowForecast(_model, isModel: true);
        else
            ShowForecast(_overall, isModel: false);
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

        StateText.Text = Loc.Get(level switch
        {
            "outage" => "LoadOutage",
            "degraded" => "LoadDegraded",
            _ => "LoadNormal",
        });

        var detail = Loc.Get(level switch
        {
            "outage" => "LoadOutageDetail",
            "degraded" => "LoadDegradedDetail",
            _ => "LoadNormalDetail",
        });

        var reason = ReasonText(go?.Reason);
        if (reason.Length > 0)
            detail += Loc.Format("LoadDetailReasonFormat", reason);

        if (go?.Models is { Count: > 0 } models)
        {
            var names = models
                .Select(m => MainWindow.FriendlyModel(m.Id))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            detail += Loc.Format("LoadDetailModelsFormat", string.Join(", ", names));
        }

        StateDetail.Text = detail;
    }

    private void ShowForecast(LoadForecast forecast, bool isModel)
    {
        _currentForecast = forecast;
        _currentIsModel = isModel;

        var scope = isModel
            ? _modelName ?? Loc.Get("ScopeMyModel")
            : Loc.Get("ScopeAll");

        ChartCaption.Text = Loc.Format("ChartCaptionFormat", scope);

        BuildChart(forecast);

        SummaryText.Text = forecast.HasPattern
            ? Loc.Format("ChartPeakFormat", forecast.PeakRange, forecast.QuietRange)
            : Loc.Get("ChartNoPeak");

        NoteText.Text = Loc.Format(
            isModel ? "ChartNoteModelFormat" : "ChartNoteAllFormat",
            forecast.Days);
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
                ToolTip = Loc.Format(
                    "ChartTooltipFormat",
                    hour.ToString("00"),
                    (share * 100).ToString("0")),
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
            "upstream" => Loc.Get("ReasonUpstream"),
            "latency" => Loc.Get("ReasonLatency"),
            "errors" => Loc.Get("ReasonErrors"),
            "availability" => Loc.Get("ReasonAvailability"),
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
