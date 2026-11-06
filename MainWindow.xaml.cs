using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using OpenCodeWidget.Controls;
using OpenCodeWidget.Models;
using OpenCodeWidget.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OpenCodeWidget;

public partial class MainWindow : Window
{
    private enum ErrorAction
    {
        Retry,
        Settings,
        Console,
    }

    private enum LoadLevel
    {
        Ok,
        Degraded,
        Outage,
    }

    private const double NormalWindowWidth = 412;
    private const double CompactWindowWidth = 332;
    private const double TaskbarBarWidth = 300;
    private const double TaskbarBarHeight = 36;

    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly Dictionary<string, string> ModelNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["deepseek-v4.1-flash"] = "DeepSeek V4.1 Flash",
        ["deepseek-v4-pro"] = "DeepSeek V4 Pro",
        ["gpt-6-luna"] = "GPT 6 Luna",
        ["gpt-5.6-luna"] = "GPT 5.6 Luna",
        ["glm-5.3"] = "GLM-5.3",
        ["glm-5.3-flash"] = "GLM-5.3 Flash",
        ["glm-5.2"] = "GLM-5.2",
        ["kimi-k3"] = "Kimi K3",
        ["kimi-k3.1-pro"] = "Kimi K3.1 Pro",
        ["kimi-k2.7-code"] = "Kimi K2.7 Code",
        ["qwen3.8-max"] = "Qwen3.8 Max",
        ["qwen3.8-flash"] = "Qwen3.8 Flash",
        ["grok-4.7"] = "Grok 4.7",
        ["minimax-m3"] = "MiniMax M3",
        ["minimax-m3.1-flash"] = "MiniMax M3.1 Flash",
    };

    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _savePositionTimer;
    private readonly DispatcherTimer _fullscreenTimer;
    private readonly DispatcherTimer _taskbarTimer;
    private readonly ForegroundWatcher _foregroundWatcher;

    private GoApiClient _api;
    private readonly StatusApiClient _statusApi = new();
    private CancellationTokenSource? _refreshCts;
    private GoSnapshot? _lastSnapshot;
    private string? _loadSummary;
    private ErrorAction _errorAction = ErrorAction.Retry;
    private int _refreshGeneration;
    private bool _refreshing;
    private bool _exiting;
    private bool _settingsPrompted;
    private bool _settingsOpen;
    private bool _fullscreenActive;
    private bool _autoHidden;
    private double? _lastNormalLeft;
    private double? _lastNormalTop;
    private IntPtr _taskbarOwner = IntPtr.Zero;
    private bool _limitStateInitialized;
    private readonly Dictionary<string, LimitState> _limitState = new(StringComparer.Ordinal);
    private LimitToastWindow? _limitToast;
    private DispatcherTimer? _testAlertTimer;

    private sealed class LimitState
    {
        public DateTimeOffset ResetsAt { get; init; }
        public int Level { get; set; }
    }

    public MainWindow(SettingsStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;

        InitializeComponent();

        _api = new GoApiClient(store.GetApiKey(settings) ?? "");
        LimitSoundPlayer.Preload(_settings.NotifySoundId);

        _refreshTimer = new DispatcherTimer();
        _refreshTimer.Tick += (_, _) => _ = RefreshAsync();

        _savePositionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _savePositionTimer.Tick += (_, _) =>
        {
            _savePositionTimer.Stop();
            SaveWindowPosition();
        };

        _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _fullscreenTimer.Tick += (_, _) => CheckFullscreen();
        _fullscreenTimer.Start();

        _taskbarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _taskbarTimer.Tick += (_, _) =>
        {
            if (_settings.TaskbarMode)
                PositionTaskbar();
        };

        _foregroundWatcher = new ForegroundWatcher();
        _foregroundWatcher.ForegroundChanged += OnForegroundChanged;
        _foregroundWatcher.Start();
        Closed += (_, _) => _foregroundWatcher.Dispose();

        ApplySettings();
        ApplyMode(initial: true);
        PositionWindow();
        ApplyCachedSnapshot();

        Loaded += OnLoaded;
        Closing += OnClosing;
        LocationChanged += (_, _) =>
        {
            if (!IsVisible)
                return;
            _savePositionTimer.Stop();
            _savePositionTimer.Start();
        };

        if (Application.Current is { } application)
            application.SessionEnding += (_, _) => _exiting = true;
    }

    public bool HasKey => _api.HasKey;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        BeginAnimation(OpacityProperty, fade);

        if (!_api.HasKey)
        {
            ShowEmptyState();
            ShowBanner(
                "Ключ OpenCode Go не найден. Вставьте его в настройках.",
                ErrorAction.Settings,
                "Нужен API-ключ");
            SetLive(false);
            PromptForSettings();
            return;
        }

        await RefreshAsync();
    }

    private void ApplySettings()
    {
        Topmost = _settings.TaskbarMode || _settings.AlwaysOnTop;
        _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.RefreshSeconds, 30, 3600));
        _refreshTimer.Start();
        _api = new GoApiClient(_store.GetApiKey(_settings) ?? "");
        FooterText.Text = "Автообновление: " + FormatInterval(_settings.RefreshSeconds);
        UpdateAlertGlow(MaxAlertLevel(_lastSnapshot?.Status?.Access?.Meters));
        LimitSoundPlayer.Preload(_settings.NotifySoundId);

        if (!_settings.NotifyOnLimits)
            _limitToast?.HideToast();
    }

    private void ApplyCachedSnapshot()
    {
        var cached = _store.LoadSnapshot();
        if (cached?.Status is null)
            return;

        _lastSnapshot = cached;
        ApplySnapshot(cached, cached: true);
    }

    public async Task RefreshAsync()
    {
        if (_refreshing)
            return;

        if (!_api.HasKey)
        {
            ShowEmptyState();
            ShowBanner(
                "Ключ OpenCode Go не найден. Вставьте его в настройках.",
                ErrorAction.Settings,
                "Нужен API-ключ");
            SetLive(false);
            return;
        }

        _refreshing = true;
        var generation = ++_refreshGeneration;
        SetRefreshing(true);

        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();
        var token = _refreshCts.Token;

        try
        {
            var status = await _api.GetStatusAsync(token);
            var activity = await _api.GetActivityAsync(token);
            var serviceStatus = await _statusApi.GetAsync(token);
            token.ThrowIfCancellationRequested();

            if (generation != _refreshGeneration)
                return;

            if (serviceStatus is not null)
                ApplyServiceStatus(serviceStatus);

            var snapshot = new GoSnapshot
            {
                Status = status,
                Activity = activity,
                FetchedAt = DateTimeOffset.Now,
            };

            _lastSnapshot = snapshot;
            _store.SaveSnapshot(snapshot);
            ApplySnapshot(snapshot, cached: false);
            EvaluateLimitAlerts(snapshot);
            SetLive(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer refresh replaced this one.
        }
        catch (GoApiException ex)
        {
            if (generation == _refreshGeneration)
                HandleApiError(ex);
        }
        catch (Exception ex)
        {
            if (generation == _refreshGeneration)
            {
                App.Log(ex);
                ShowBanner(
                    "Не удалось обновить данные. Показаны последние значения.",
                    ErrorAction.Retry,
                    "Ошибка обновления");
                SetLive(false);
            }
        }
        finally
        {
            if (generation == _refreshGeneration)
            {
                _refreshing = false;
                SetRefreshing(false);
            }
        }
    }

    private void ApplySnapshot(GoSnapshot snapshot, bool cached)
    {
        var status = snapshot.Status;

        if (status?.Access?.Meters is { } meters)
        {
            ApplyMeter(FiveHourMeter, meters.FiveHour, "5 часов", "5-часовое окно");
            ApplyMeter(WeekMeter, meters.Week, "Неделя", "Недельное окно");
            ApplyMeter(MonthMeter, meters.Month, "Месяц", "Месячное окно");

            ApplyCompactMeter(CompactFiveHour, meters.FiveHour, "5ч", "5-часовое окно");
            ApplyCompactMeter(CompactWeek, meters.Week, "нед", "Недельное окно");
            ApplyCompactMeter(CompactMonth, meters.Month, "мес", "Месячное окно");

            var plan = PlanName(status.Product);
            PlanBadge.Text = plan;
            CompactPlanBadge.Text = plan;
            TaskbarPlanBadge.Text = plan;

            UpdateTaskbarMeters(meters);

            var renewal = status.Access.EndsAt.ToLocalTime().ToString("d MMM", Ru);
            var ended = status.CancelAtPeriodEnd || status.Access.CancelAtPeriodEnd;
            var planText = ended
                ? $"{plan} · доступ до {renewal}"
                : $"{plan} · продление {renewal}";

            SubtitleText.Text = planText;
            var planTooltip = planText;

            if (status.UseBalance)
            {
                SubtitleText.Text += " · extra";
                planTooltip += " · extra usage";
            }

            PlanBadgeHost.ToolTip = planTooltip;
            CompactPlanBadgeHost.ToolTip = planTooltip;
            TaskbarPlanBadge.ToolTip = planTooltip;

            if (!cached)
                HideBanner();
        }
        else
        {
            var plan = PlanName(status?.Product);
            PlanBadge.Text = plan;
            CompactPlanBadge.Text = plan;
            TaskbarPlanBadge.Text = plan;
            PlanBadgeHost.ToolTip = null;
            CompactPlanBadgeHost.ToolTip = null;
            TaskbarPlanBadge.ToolTip = null;

            UpdateTaskbarMeters(null);
            ShowEmptyState("нет данных");
            SubtitleText.Text = "Подписка Go не активна";

            if (!cached)
                ShowBanner(
                    "Активная подписка Go не найдена. Проверьте её в консоли.",
                    ErrorAction.Console,
                    "Нет подписки Go");
        }

        UpdateAlertGlow(MaxAlertLevel(status?.Access?.Meters));
        UpdateActivity(snapshot.Activity);
        UpdatedText.Text = FormatUpdated(snapshot.FetchedAt);
        CompactUpdatedText.Text = FormatUpdated(snapshot.FetchedAt);
        FooterText.Text = cached
            ? "Данные из кэша · обновление..."
            : "Автообновление: " + FormatInterval(_settings.RefreshSeconds);
    }

    private static void ApplyMeter(RingMeter ring, GoMeter? meter, string title, string caption)
    {
        if (meter is null || meter.LimitMicroCents <= 0)
        {
            ring.SetEmpty(title, "нет данных");
            return;
        }

        var resetAt = meter.ResetsAt.ToLocalTime().ToString("d MMM, HH:mm", Ru);
        var tooltip =
            $"{caption}: {FormatUsd(meter.UsedUsd)} из {FormatUsd(meter.LimitUsd)}. " +
            $"Сброс {resetAt} (через {FormatCountdown(meter.ResetsAt)}).";

        ring.SetData(meter.Percent, title, FormatCountdown(meter.ResetsAt), tooltip);
    }

    private static void ApplyCompactMeter(MiniMeter meter, GoMeter? data, string title, string caption)
    {
        if (data is null || data.LimitMicroCents <= 0)
        {
            meter.SetEmpty(title, "нет данных");
            return;
        }

        var resetAt = data.ResetsAt.ToLocalTime().ToString("d MMM, HH:mm", Ru);
        var tooltip =
            $"{caption}: {FormatUsd(data.UsedUsd)} из {FormatUsd(data.LimitUsd)}. " +
            $"Сброс {resetAt} (через {FormatCountdown(data.ResetsAt)}).";

        meter.SetData(data.Percent, title, FormatCompactCountdown(data.ResetsAt), tooltip);
    }

    private void UpdateTaskbarMeters(GoMeters? meters)
    {
        if (meters is null)
        {
            TaskbarFiveHour.Text = "-";
            TaskbarWeek.Text = "-";
            TaskbarMonth.Text = "-";
            TaskbarCard.ToolTip = string.IsNullOrEmpty(_loadSummary)
                ? "OpenCode Go"
                : "OpenCode Go\n" + _loadSummary;
            return;
        }

        TaskbarFiveHour.Text = FormatShortPercent(meters.FiveHour?.Percent);
        TaskbarWeek.Text = FormatShortPercent(meters.Week?.Percent);
        TaskbarMonth.Text = FormatShortPercent(meters.Month?.Percent);

        var tooltip =
            $"OpenCode Go · 5ч {TaskbarFiveHour.Text} ({FormatMeterUsd(meters.FiveHour)}) · " +
            $"нед {TaskbarWeek.Text} ({FormatMeterUsd(meters.Week)}) · " +
            $"мес {TaskbarMonth.Text} ({FormatMeterUsd(meters.Month)})";

        if (!string.IsNullOrEmpty(_loadSummary))
            tooltip += "\n" + _loadSummary;

        TaskbarCard.ToolTip = tooltip;
    }

    private void ApplyServiceStatus(ServiceStatusResponse status)
    {
        var go = status.Components.FirstOrDefault(c =>
            string.Equals(c.Service, "go", StringComparison.OrdinalIgnoreCase));

        var state = go?.Status;
        if (string.IsNullOrWhiteSpace(state))
            state = status.Status;

        var level = state?.Trim().ToLowerInvariant() switch
        {
            "outage" => LoadLevel.Outage,
            "degraded" => LoadLevel.Degraded,
            _ => LoadLevel.Ok,
        };

        if (level == LoadLevel.Ok)
        {
            _loadSummary = null;
            SetLoadVisible(false, LoadLevel.Ok, null);
            return;
        }

        var reason = ReasonText(go?.Reason);
        var models = go?.Models?
            .Select(m => FriendlyModel(m.Id))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        var lines = new List<string>
        {
            level == LoadLevel.Outage
                ? "OpenCode Go: сбой"
                : "OpenCode Go: повышенная нагрузка",
        };

        if (reason.Length > 0)
            lines.Add("Причина: " + reason);

        if (models.Length > 0)
            lines.Add("Модели: " + string.Join(", ", models));

        if (status.Stale)
            lines.Add("Данные статуса могли устареть");

        lines.Add("Обновлено: " + DateTimeOffset.Now.ToString("HH:mm", Ru));

        _loadSummary = $"Нагрузка: {reason}";
        SetLoadVisible(true, level, string.Join("\n", lines));
    }

    private void SetLoadVisible(bool visible, LoadLevel level, string? tooltip)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        LoadChip.Visibility = visibility;
        CompactLoadDot.Visibility = visibility;
        TaskbarLoadDot.Visibility = visibility;

        var brush = (Brush)FindResource(level == LoadLevel.Outage ? "DangerBrush" : "WarningBrush");

        LoadChipDot.Fill = brush;
        CompactLoadDot.Fill = brush;
        TaskbarLoadDot.Fill = brush;

        if (brush is SolidColorBrush solid)
        {
            var color = solid.Color;
            LoadChip.Background = new SolidColorBrush(Color.FromArgb(0x2E, color.R, color.G, color.B));
            LoadChip.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, color.R, color.G, color.B));
        }

        LoadChipText.Text = level == LoadLevel.Outage ? "Сбой" : "Нагрузка";
        LoadChipText.Foreground = brush;

        LoadChip.ToolTip = tooltip;
        CompactLoadDot.ToolTip = tooltip;
        TaskbarLoadDot.ToolTip = tooltip;
    }

    private static string ReasonText(string? reason)
    {
        return reason?.Trim().ToLowerInvariant() switch
        {
            "upstream" => "проблема на стороне провайдера",
            "latency" => "повышенные задержки",
            "errors" => "ошибки запросов",
            "availability" => "недоступность",
            "none" or null or "" => "",
            _ => reason!,
        };
    }

    private static string FormatMeterUsd(GoMeter? meter)
    {
        return meter is null
            ? "-"
            : $"{FormatUsd(meter.UsedUsd)} из {FormatUsd(meter.LimitUsd)}";
    }

    private static string FormatShortPercent(double? percent)
    {
        if (percent is null)
            return "-";

        var value = Math.Clamp(percent.Value, 0, 100);
        if (value > 0 && value < 1)
            return "<1%";
        if (value < 10)
            return value.ToString("0.0", Ru) + "%";
        return Math.Round(value).ToString("0", Ru) + "%";
    }

    private void UpdateActivity(GoActivityResponse? activity)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var day = activity?.Daily?.LastOrDefault(d =>
                      string.Equals(d.Date, today, StringComparison.Ordinal))
                  ?? activity?.Daily?.LastOrDefault();

        if (day is null)
        {
            RequestsValue.Text = "-";
            TokensValue.Text = "-";
            CostValue.Text = "-";
            CompactTodayText.Text = "Сегодня: -";
        }
        else
        {
            RequestsValue.Text = day.TotalRequests.ToString("N0", Ru);
            TokensValue.Text = FormatTokens(day.TotalTokens);
            CostValue.Text = FormatUsd(day.TotalCostMicroCents / 100_000_000m);
            CompactTodayText.Text =
                $"Сегодня: {day.TotalRequests.ToString("N0", Ru)} · {FormatUsd(day.TotalCostMicroCents / 100_000_000m)}";
        }

        var top = activity?.Models?
            .OrderByDescending(m => m.TotalRequests)
            .FirstOrDefault();

        TopModelText.Text = top is null
            ? ""
            : $"{FriendlyModel(top.Model)} · {top.TotalRequests.ToString("N0", Ru)}";
    }

    private void HandleApiError(GoApiException exception)
    {
        SetLive(false);

        switch (exception.Kind)
        {
            case GoApiErrorKind.Unauthorized:
                SubtitleText.Text = "Нет доступа";
                ShowBanner(
                    "Ключ не подходит или у него нет доступа к статистике. Проверьте ключ в настройках.",
                    ErrorAction.Settings,
                    "Ключ не подходит");
                break;

            case GoApiErrorKind.RateLimited:
                ShowBanner(
                    "Слишком много запросов. Подождите минуту и попробуйте снова.",
                    ErrorAction.Retry,
                    "Слишком много запросов");
                break;

            case GoApiErrorKind.Network:
                ShowBanner(
                    "Нет связи с opencode.ai. Показаны последние данные.",
                    ErrorAction.Retry,
                    "Нет связи");
                break;

            default:
                ShowBanner(
                    "Сервис временно недоступен. Показаны последние данные.",
                    ErrorAction.Retry,
                    "Сервис недоступен");
                break;
        }

        if (_lastSnapshot is null)
        {
            ShowEmptyState("нет данных");
            SubtitleText.Text = "Нет данных";
        }
    }

    private void ShowEmptyState(string message = "нет ключа")
    {
        FiveHourMeter.SetEmpty("5 часов", message);
        WeekMeter.SetEmpty("Неделя", message);
        MonthMeter.SetEmpty("Месяц", message);
        CompactFiveHour.SetEmpty("5ч", message);
        CompactWeek.SetEmpty("нед", message);
        CompactMonth.SetEmpty("мес", message);
        RequestsValue.Text = "-";
        TokensValue.Text = "-";
        CostValue.Text = "-";
        TopModelText.Text = "";
        CompactTodayText.Text = "Сегодня: -";
        TaskbarFiveHour.Text = "-";
        TaskbarWeek.Text = "-";
        TaskbarMonth.Text = "-";
        TaskbarCard.ToolTip = message;
        StopAlertGlow();
    }

    private void SetLive(bool ok)
    {
        var brush = (Brush)(ok ? FindResource("SuccessBrush") : FindResource("DangerBrush"));
        LiveDot.Fill = brush;
        CompactLiveDot.Fill = brush;
        TaskbarLiveDot.Fill = brush;
    }

    private void CheckFullscreen()
    {
        if (_settingsOpen)
        {
            _fullscreenActive = false;
            return;
        }

        if (!_settings.HideInFullscreen)
        {
            if (_autoHidden)
            {
                _autoHidden = false;
                ShowFromTray();
            }

            _fullscreenActive = false;
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        var fullscreen = FullscreenDetector.IsFullscreenOnSameMonitor(handle);

        if (fullscreen)
        {
            if (_fullscreenActive)
                return;

            _fullscreenActive = true;
            _limitToast?.HideToast();

            if (IsVisible)
            {
                _autoHidden = true;
                Hide();
            }

            return;
        }

        if (!_fullscreenActive)
            return;

        _fullscreenActive = false;

        if (_autoHidden)
        {
            _autoHidden = false;
            ShowFromTray();
        }
    }

    private void SetRefreshing(bool refreshing)
    {
        RefreshButton.IsEnabled = !refreshing;

        if (refreshing)
        {
            var rotate = new RotateTransform(0);
            RefreshGlyph.RenderTransform = rotate;
            var animation = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            rotate.BeginAnimation(RotateTransform.AngleProperty, animation);
        }
        else
        {
            RefreshGlyph.RenderTransform = Transform.Identity;
        }
    }

    private void EvaluateLimitAlerts(GoSnapshot snapshot)
    {
        var meters = snapshot.Status?.Access?.Meters;
        if (meters is null)
            return;

        var entries = new (string Key, string Short, GoMeter? Meter)[]
        {
            ("fiveHour", "5ч", meters.FiveHour),
            ("week", "нед", meters.Week),
            ("month", "мес", meters.Month),
        };

        var initialize = !_limitStateInitialized;
        var alerts = new List<(string Short, GoMeter Meter, int Level)>();

        foreach (var (key, shortLabel, meter) in entries)
        {
            if (meter is null)
                continue;

            var level = HighestThreshold(meter.Percent);

            if (!_limitState.TryGetValue(key, out var state) || state.ResetsAt != meter.ResetsAt)
            {
                _limitState[key] = new LimitState { ResetsAt = meter.ResetsAt, Level = level };
                if (!initialize && level > 0)
                    alerts.Add((shortLabel, meter, level));
                continue;
            }

            if (level <= state.Level)
                continue;

            state.Level = level;
            if (!initialize && level > 0)
                alerts.Add((shortLabel, meter, level));
        }

        _limitStateInitialized = true;

        if (alerts.Count == 0 || !_settings.NotifyOnLimits)
            return;

        var top = alerts
            .OrderByDescending(a => a.Level)
            .ThenByDescending(a => a.Meter.Percent)
            .First();

        var reset = FormatCountdown(top.Meter.ResetsAt);
        var text = top.Level >= 100
            ? $"{top.Short}: лимит исчерпан · сброс {reset}"
            : $"{top.Short}: {FormatAlertPercent(top.Meter.Percent)} · сброс {reset}";

        if (_fullscreenActive)
            return;

        ShowLimitToast(text, top.Level);
        PlayLimitSound();
    }

    private void ShowLimitToast(string text, int level, bool force = false)
    {
        if (!force && (!_settings.NotifyOnLimits || _fullscreenActive))
            return;

        _limitToast ??= new LimitToastWindow();

        var mainRect = IsVisible && ActualWidth > 1
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : Rect.Empty;

        var fallbackWidth = _settings.CompactMode ? CompactWindowWidth : NormalWindowWidth;
        _limitToast.ShowMessage(text, level, mainRect, fallbackWidth);
    }

    public void ShowTestNotification(string? soundId = null, bool playSound = true)
    {
        const string text = "5ч: 92% · сброс 1 ч 10 мин";

        _limitToast ??= new LimitToastWindow();
        var fallbackWidth = _settings.CompactMode ? CompactWindowWidth : NormalWindowWidth;
        _limitToast.ShowMessage(text, 90, Rect.Empty, fallbackWidth);

        UpdateAlertGlow(90);

        _testAlertTimer?.Stop();
        _testAlertTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _testAlertTimer.Tick += (_, _) =>
        {
            _testAlertTimer?.Stop();
            UpdateAlertGlow(MaxAlertLevel(_lastSnapshot?.Status?.Access?.Meters));
        };
        _testAlertTimer.Start();

        if (playSound)
            LimitSoundPlayer.Play(soundId ?? _settings.NotifySoundId);
    }

    private void PlayLimitSound()
    {
        if (!_settings.NotifySound)
            return;

        LimitSoundPlayer.Play(_settings.NotifySoundId);
    }

    private void UpdateAlertGlow(int level)
    {
        if (!_settings.AnimateOnLimit || level < 70)
        {
            StopAlertGlow();
            return;
        }

        var brush = (Brush)FindResource(level >= 90 ? "DangerBrush" : "WarningBrush");
        var color = brush is SolidColorBrush solid ? solid.Color : Colors.OrangeRed;

        AlertGlow.BorderBrush = brush;
        AlertGlow.Opacity = 0.9;
        AlertGlow.Effect = new DropShadowEffect
        {
            Color = color,
            BlurRadius = 22,
            ShadowDepth = 0,
            Opacity = 0.4,
        };
    }

    private void StopAlertGlow()
    {
        AlertGlow.BeginAnimation(OpacityProperty, null);
        AlertGlow.Opacity = 0;
        AlertGlow.Effect = null;
    }

    private static int MaxAlertLevel(GoMeters? meters)
    {
        if (meters is null)
            return 0;

        return new[] { meters.FiveHour, meters.Week, meters.Month }
            .Where(m => m is not null)
            .Select(m => HighestThreshold(m!.Percent))
            .DefaultIfEmpty(0)
            .Max();
    }

    private static int HighestThreshold(double percent)
    {
        if (percent >= 100)
            return 100;
        if (percent >= 90)
            return 90;
        if (percent >= 70)
            return 70;
        return 0;
    }

    private static string FormatAlertPercent(double percent)
    {
        return Math.Clamp(percent, 0, 100).ToString("0.#", Ru) + "%";
    }

    private void ShowBanner(string text, ErrorAction action, string? compactText = null)
    {
        _errorAction = action;
        ErrorText.Text = text;
        ErrorActionButton.Content = action switch
        {
            ErrorAction.Settings => "Настройки",
            ErrorAction.Console => "Консоль",
            _ => "Повторить",
        };
        ErrorBanner.Visibility = Visibility.Visible;

        CompactErrorText.Text = compactText ?? text;
        CompactError.ToolTip = text;
        CompactError.Visibility = Visibility.Visible;
        TaskbarCard.ToolTip = text;
    }

    private void HideBanner()
    {
        ErrorBanner.Visibility = Visibility.Collapsed;
        CompactError.Visibility = Visibility.Collapsed;
    }

    private void PromptForSettings()
    {
        if (_settingsPrompted)
            return;

        _settingsPrompted = true;
        Dispatcher.BeginInvoke(
            new Action(OpenSettings),
            DispatcherPriority.ApplicationIdle);
    }

    public void ShowFromTray()
    {
        _autoHidden = false;

        if (!IsVisible)
            Show();

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
        Topmost = false;
        Topmost = _settings.TaskbarMode || _settings.AlwaysOnTop;
    }

    public void OpenSettings()
    {
        ShowFromTray();

        var dialog = new SettingsWindow(_store, _settings)
        {
            Owner = this,
        };

        bool? result;
        _settingsOpen = true;

        try
        {
            result = dialog.ShowDialog();
        }
        finally
        {
            _settingsOpen = false;
        }

        if (result != true)
            return;

        _api = new GoApiClient(_store.GetApiKey(_settings) ?? "");
        ApplySettings();
        _store.ClearSnapshot();
        _lastSnapshot = null;
        _limitState.Clear();
        _limitStateInitialized = false;
        HideBanner();
        SetLive(false);

        _refreshCts?.Cancel();
        _refreshing = false;
        _ = RefreshAsync();
    }

    public void ToggleCompactMode()
    {
        if (_settings.TaskbarMode)
        {
            SetMode(WidgetMode.Compact);
            return;
        }

        _settings.CompactMode = !_settings.CompactMode;
        ApplyMode(initial: false);

        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;

        try
        {
            _store.Save(_settings);
        }
        catch
        {
            // The mode still applies for this session.
        }
    }

    public void SetMode(WidgetMode mode)
    {
        var wasTaskbar = _settings.TaskbarMode;

        if (!wasTaskbar && mode == WidgetMode.Taskbar)
        {
            _lastNormalLeft = Left;
            _lastNormalTop = Top;
        }

        _settings.TaskbarMode = mode == WidgetMode.Taskbar;
        _settings.CompactMode = mode == WidgetMode.Compact;

        ApplyMode(initial: false);

        if (wasTaskbar && !_settings.TaskbarMode)
        {
            _settings.WindowLeft = _lastNormalLeft;
            _settings.WindowTop = _lastNormalTop;
            PositionWindow();
        }

        if (!_settings.TaskbarMode)
        {
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
        }

        try
        {
            _store.Save(_settings);
        }
        catch
        {
            // The mode still applies for this session.
        }
    }

    private void ApplyMode(bool initial)
    {
        var taskbar = _settings.TaskbarMode;
        var compact = !taskbar && _settings.CompactMode;

        NormalCard.Visibility = !taskbar && !compact ? Visibility.Visible : Visibility.Collapsed;
        CompactCard.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        TaskbarCard.Visibility = taskbar ? Visibility.Visible : Visibility.Collapsed;

        AlertGlow.Margin = taskbar ? new Thickness(0) : new Thickness(16);
        AlertGlow.CornerRadius = taskbar ? new CornerRadius(10) : new CornerRadius(18);
        Topmost = taskbar || _settings.AlwaysOnTop;

        if (taskbar)
        {
            MaxWidth = TaskbarBarWidth;
            SizeToContent = SizeToContent.Width;
            Width = double.NaN;
            Height = TaskbarBarHeight;
            _taskbarOwner = IntPtr.Zero;
            _taskbarTimer.Start();
            PositionTaskbar();
            return;
        }

        _taskbarTimer.Stop();

        if (_taskbarOwner != IntPtr.Zero)
        {
            TaskbarLocator.SetOwner(new WindowInteropHelper(this).Handle, IntPtr.Zero);
            _taskbarOwner = IntPtr.Zero;
        }

        MaxWidth = double.PositiveInfinity;
        SizeToContent = SizeToContent.Height;
        Height = double.NaN;

        var targetWidth = compact ? CompactWindowWidth : NormalWindowWidth;
        if (Math.Abs(Width - targetWidth) < 0.1)
            return;

        var oldWidth = ActualWidth > 1 ? ActualWidth : Width;
        var oldRight = Left + oldWidth;
        Width = targetWidth;

        if (initial || !IsLoaded)
            return;

        var work = SystemParameters.WorkArea;
        var left = oldRight - targetWidth;
        left = Math.Max(work.Left + 8, Math.Min(left, work.Right - targetWidth - 8));
        Left = left;
    }

    private void PositionTaskbar()
    {
        var taskbar = TaskbarLocator.GetPrimaryTaskbar();
        var handle = new WindowInteropHelper(this).Handle;

        if (handle == IntPtr.Zero)
            return;

        if (taskbar is null)
        {
            var work = SystemParameters.WorkArea;
            SetTaskbarBounds(
                work.Left + 12,
                work.Bottom - TaskbarBarHeight - 12,
                TaskbarBarHeight,
                TaskbarBarWidth);

            if (_taskbarOwner != IntPtr.Zero)
            {
                TaskbarLocator.SetOwner(handle, IntPtr.Zero);
                _taskbarOwner = IntPtr.Zero;
            }

            TaskbarLocator.KeepOnTop(handle);
            return;
        }

        var barHeight = Math.Min(TaskbarBarHeight, taskbar.Height - 8);
        var barWidth = Math.Min(TaskbarBarWidth, taskbar.Width - 24);

        SetTaskbarBounds(
            taskbar.Left + 12,
            taskbar.Top + (taskbar.Height - barHeight) / 2,
            barHeight,
            barWidth);

        if (_taskbarOwner != taskbar.Handle)
        {
            TaskbarLocator.SetOwner(handle, taskbar.Handle);
            _taskbarOwner = taskbar.Handle;
            TaskbarLocator.KeepOnTop(handle);
        }
    }

    private void OnForegroundChanged()
    {
        if (!_settings.TaskbarMode)
            return;

        Dispatcher.BeginInvoke(new Action(RaiseTaskbarBar), DispatcherPriority.Send);
    }

    private void RaiseTaskbarBar()
    {
        if (!_settings.TaskbarMode || _fullscreenActive)
            return;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || FullscreenDetector.IsFullscreenOnSameMonitor(handle))
            return;

        var taskbar = TaskbarLocator.GetPrimaryTaskbar();
        if (taskbar is null)
            return;

        if (_taskbarOwner != taskbar.Handle)
        {
            TaskbarLocator.SetOwner(handle, taskbar.Handle);
            _taskbarOwner = taskbar.Handle;
        }

        TaskbarLocator.KeepOnTop(handle);
    }

    private void SetTaskbarBounds(double left, double top, double height, double maxWidth)
    {
        if (Math.Abs(Left - left) > 0.1)
            Left = left;

        if (Math.Abs(Top - top) > 0.1)
            Top = top;

        if (Math.Abs(Height - height) > 0.1)
            Height = height;

        if (Math.Abs(MaxWidth - maxWidth) > 0.1)
            MaxWidth = maxWidth;
    }

    private void ModeNormal_Click(object sender, RoutedEventArgs e)
    {
        SetMode(WidgetMode.Normal);
    }

    private void ModeCompact_Click(object sender, RoutedEventArgs e)
    {
        SetMode(WidgetMode.Compact);
    }

    private void ModeTaskbar_Click(object sender, RoutedEventArgs e)
    {
        SetMode(WidgetMode.Taskbar);
    }

    private void TaskbarCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null)
            return;

        if (e.ClickCount != 2)
            return;

        SetMode(WidgetMode.Normal);
        e.Handled = true;
    }

    private void CompactToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleCompactMode();
    }

    private void CompactError_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            ErrorAction_Click(sender, e);
    }

    public void RequestExit()
    {
        _exiting = true;
        SaveWindowPosition();
        _refreshCts?.Cancel();
        _refreshTimer.Stop();
        _savePositionTimer.Stop();
        _fullscreenTimer.Stop();
        _taskbarTimer.Stop();
        _foregroundWatcher.Dispose();
        _limitToast?.Close();
        Close();
        Application.Current.Shutdown();
    }

    private void PositionWindow()
    {
        if (_settings.TaskbarMode)
            return;

        var work = SystemParameters.WorkArea;
        var width = Width > 0 ? Width : 412;

        var left = work.Right - width - 16;
        var top = work.Top + 24;

        if (_settings.WindowLeft is { } savedLeft && _settings.WindowTop is { } savedTop)
        {
            var virtualLeft = SystemParameters.VirtualScreenLeft;
            var virtualTop = SystemParameters.VirtualScreenTop;
            var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

            var visible =
                savedLeft >= virtualLeft - width + 100 &&
                savedLeft <= virtualRight - 100 &&
                savedTop >= virtualTop - 40 &&
                savedTop <= virtualBottom - 100;

            if (visible)
            {
                left = savedLeft;
                top = savedTop;
            }
        }

        Left = left;
        Top = top;
    }

    private void SaveWindowPosition()
    {
        if (_settings.TaskbarMode || !IsVisible || double.IsNaN(Left) || double.IsNaN(Top))
            return;

        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;

        try
        {
            _store.Save(_settings);
        }
        catch
        {
            // Position is not critical.
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting)
        {
            SaveWindowPosition();
            return;
        }

        _autoHidden = false;
        e.Cancel = true;
        SaveWindowPosition();
        Hide();
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        if (PresentationSource.FromVisual(menu) is HwndSource source)
        {
            TaskbarLocator.ClampToWorkArea(source.Handle);
            TaskbarLocator.KeepOnTop(source.Handle);
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null)
            return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The mouse button was released before the drag started.
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        _autoHidden = false;
        SaveWindowPosition();
        Hide();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        RequestExit();
    }

    private void ErrorAction_Click(object sender, RoutedEventArgs e)
    {
        switch (_errorAction)
        {
            case ErrorAction.Settings:
                OpenSettings();
                break;
            case ErrorAction.Console:
                OpenConsole();
                break;
            default:
                _ = RefreshAsync();
                break;
        }
    }

    private void ConsoleLink_Click(object sender, RoutedEventArgs e)
    {
        OpenConsole();
    }

    private void StatusChip_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

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

    private static void OpenConsole()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://opencode.ai/console")
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
            SaveWindowPosition();
            Hide();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _ = RefreshAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.M && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ToggleCompactMode();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SetMode(_settings.TaskbarMode ? WidgetMode.Normal : WidgetMode.Taskbar);
            e.Handled = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static string PlanName(string? product)
    {
        return product switch
        {
            "go" => "Go",
            "go-plus" => "Go Plus",
            "go-teams" => "Go Teams",
            null or "" => "Go",
            _ => product,
        };
    }

    private static string FormatCountdown(DateTimeOffset resetsAt)
    {
        var left = resetsAt - DateTimeOffset.Now;
        if (left <= TimeSpan.Zero)
            return "сброс";

        if (left.TotalDays >= 1)
        {
            var days = (int)left.TotalDays;
            return left.Hours > 0 ? $"{days} д {left.Hours} ч" : $"{days} дн";
        }

        if (left.TotalHours >= 1)
            return $"{(int)left.TotalHours} ч {left.Minutes} мин";

        return $"{Math.Max(1, left.Minutes)} мин";
    }

    private static string FormatUpdated(DateTimeOffset fetchedAt)
    {
        var local = fetchedAt.ToLocalTime();
        return local.Date == DateTime.Today
            ? local.ToString("HH:mm", Ru)
            : local.ToString("d MMM, HH:mm", Ru);
    }

    private static string FormatCompactCountdown(DateTimeOffset resetsAt)
    {
        var left = resetsAt - DateTimeOffset.Now;
        if (left <= TimeSpan.Zero)
            return "сброс";

        if (left.TotalDays >= 10)
            return $"{(int)left.TotalDays}д";

        if (left.TotalDays >= 1)
        {
            var days = (int)left.TotalDays;
            return left.Hours > 0 ? $"{days}д {left.Hours}ч" : $"{days}д";
        }

        if (left.TotalHours >= 1)
            return $"{(int)left.TotalHours}ч {left.Minutes}м";

        return $"{Math.Max(1, left.Minutes)}м";
    }

    private static string FormatUsd(decimal value)
    {
        if (value >= 0.01m)
            return "$" + value.ToString("0.00", Ru);
        if (value > 0)
            return "$" + value.ToString("0.000", Ru);
        return "$0,00";
    }

    private static string FormatTokens(long tokens)
    {
        if (tokens >= 1_000_000)
            return (tokens / 1_000_000d).ToString("0.0", Ru) + " млн";
        if (tokens >= 1_000)
            return (tokens / 1_000d).ToString("0.0", Ru) + " тыс.";
        return tokens.ToString("N0", Ru);
    }

    private static string FormatInterval(int seconds)
    {
        if (seconds < 60)
            return $"{seconds} с";
        if (seconds < 3600)
            return $"{seconds / 60} мин";
        return $"{seconds / 3600} ч";
    }

    private static string FriendlyModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return "";

        if (ModelNames.TryGetValue(model, out var known))
            return known;

        var text = model.Replace('-', ' ').Replace('_', ' ').Trim();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text);
    }
}
