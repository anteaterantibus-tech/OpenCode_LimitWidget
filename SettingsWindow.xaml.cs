using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenCodeWidget.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OpenCodeWidget;

public partial class SettingsWindow : Window
{
    private static CultureInfo Ru => Loc.Culture;

    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private bool _revealed;
    private bool _languageReady;
    private string _originalLanguage = Loc.Russian;

    public SettingsWindow(SettingsStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;

        InitializeComponent();

        _originalLanguage = Loc.Language;
        _languageReady = false;
        if (Loc.Language == Loc.English)
            LangEn.IsChecked = true;
        else
            LangRu.IsChecked = true;
        _languageReady = true;

        Closing += (_, _) =>
        {
            if (DialogResult != true)
                Loc.Apply(_originalLanguage);
        };

        var key = store.GetApiKey(settings) ?? "";
        SetKey(key);

        AlwaysOnTopToggle.IsChecked = settings.AlwaysOnTop;
        StartupToggle.IsChecked = StartupManager.IsEnabled();
        FullscreenToggle.IsChecked = settings.HideInFullscreen;
        NotifyToggle.IsChecked = settings.NotifyOnLimits;
        SoundToggle.IsChecked = settings.NotifySound;
        SoundToggle.IsEnabled = settings.NotifyOnLimits;
        AnimateToggle.IsChecked = settings.AnimateOnLimit;
        SelectSound(settings.NotifySoundId);

        NotifyToggle.Checked += (_, _) => SoundToggle.IsEnabled = true;
        NotifyToggle.Unchecked += (_, _) => SoundToggle.IsEnabled = false;

        var seconds = Math.Clamp(settings.RefreshSeconds, 30, 900);
        var radio = seconds switch
        {
            <= 45 => Refresh30,
            <= 180 => Refresh60,
            <= 600 => Refresh300,
            _ => Refresh900,
        };
        radio.IsChecked = true;

        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 16;
        Top = work.Top + 16;

        Loaded += (_, _) => PositionOverOwner();
    }

    private void PositionOverOwner()
    {
        if (Owner is null)
            return;

        var work = SystemParameters.WorkArea;

        var left = Owner.Left + (Owner.ActualWidth - ActualWidth) / 2;
        var top = Owner.Top + (Owner.ActualHeight - ActualHeight) / 2;

        left = Math.Max(work.Left + 16, Math.Min(left, work.Right - ActualWidth - 16));
        top = Math.Max(work.Top + 16, Math.Min(top, work.Bottom - ActualHeight - 16));

        Left = left;
        Top = top;
    }

    private string ReadKey()
    {
        return (_revealed ? KeyText.Text : KeyPassword.Password).Trim();
    }

    private void SetKey(string key)
    {
        KeyPassword.Password = key;
        KeyText.Text = key;
    }

    private int SelectedRefreshSeconds()
    {
        if (Refresh30.IsChecked == true) return 30;
        if (Refresh60.IsChecked == true) return 60;
        if (Refresh300.IsChecked == true) return 300;
        return 900;
    }

    private string SelectedSoundId()
    {
        if (SoundBell.IsChecked == true) return "bell";
        if (SoundWood.IsChecked == true) return "wood";
        if (SoundDrop.IsChecked == true) return "drop";
        if (SoundMinimal.IsChecked == true) return "minimal";
        return "soft";
    }

    private void SelectSound(string? id)
    {
        var radio = id switch
        {
            "bell" => SoundBell,
            "wood" => SoundWood,
            "drop" => SoundDrop,
            "minimal" => SoundMinimal,
            _ => SoundSoft,
        };

        radio.IsChecked = true;
    }

    private void PreviewSound_Click(object sender, RoutedEventArgs e)
    {
        LimitSoundPlayer.Play(SelectedSoundId());
    }

    private void TestNotification_Click(object sender, RoutedEventArgs e)
    {
        if (Owner is MainWindow main)
            main.ShowTestNotification(SelectedSoundId(), SoundToggle.IsChecked == true);
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        _revealed = !_revealed;

        if (_revealed)
        {
            KeyText.Text = KeyPassword.Password;
            KeyPassword.Visibility = Visibility.Collapsed;
            KeyText.Visibility = Visibility.Visible;
            RevealButton.Content = Loc.Get("Hide");
            KeyText.CaretIndex = KeyText.Text.Length;
            KeyText.Focus();
        }
        else
        {
            KeyPassword.Password = KeyText.Text;
            KeyText.Visibility = Visibility.Collapsed;
            KeyPassword.Visibility = Visibility.Visible;
            RevealButton.Content = Loc.Get("Show");
            KeyPassword.Focus();
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        ImportButton.IsEnabled = false;
        ImportStatus.Foreground = (Brush)FindResource("MutedBrush");
        ImportStatus.Text = Loc.Get("ImportSearching");

        try
        {
            var key = await OpenCodeKeyImporter.TryImportAsync();
            if (!string.IsNullOrWhiteSpace(key))
            {
                SetKey(key);
                ImportStatus.Foreground = (Brush)FindResource("SuccessBrush");
                ImportStatus.Text = Loc.Get("ImportFound");
            }
            else
            {
                ImportStatus.Foreground = (Brush)FindResource("DangerBrush");
                ImportStatus.Text = Loc.Get("ImportNotFound");
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ImportStatus.Foreground = (Brush)FindResource("DangerBrush");
            ImportStatus.Text = Loc.Get("ImportFailed");
        }
        finally
        {
            ImportButton.IsEnabled = true;
        }
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var key = ReadKey();
        if (key.Length < 16)
        {
            ShowStatus(Loc.Get("CheckNoKey"), StatusKind.Error);
            return;
        }

        SetBusy(true);
        ShowStatus(Loc.Get("CheckChecking"), StatusKind.Neutral);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            var api = new GoApiClient(key);
            var status = await api.GetStatusAsync(timeout.Token);

            if (status.Access?.Meters is not null)
            {
                var plan = status.Product switch
                {
                    "go" => "Go",
                    "go-plus" => "Go Plus",
                    "go-teams" => "Go Teams",
                    _ => "Go",
                };
                var end = status.Access.EndsAt.ToLocalTime().ToString("d MMM yyyy", Ru);
                ShowStatus(Loc.Format("CheckOkFormat", plan, end), StatusKind.Success);
            }
            else
            {
                ShowStatus(Loc.Get("CheckNoSub"), StatusKind.Error);
            }
        }
        catch (OperationCanceledException)
        {
            ShowStatus(Loc.Get("CheckTimeout"), StatusKind.Error);
        }
        catch (GoApiException ex)
        {
            ShowStatus(ex.Kind switch
            {
                GoApiErrorKind.Unauthorized => Loc.Get("CheckUnauthorized"),
                GoApiErrorKind.RateLimited => Loc.Get("CheckRateLimited"),
                GoApiErrorKind.Network => Loc.Get("CheckNetwork"),
                _ => Loc.Get("CheckService"),
            }, StatusKind.Error);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowStatus(Loc.Get("CheckError"), StatusKind.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var key = ReadKey();
        if (key.Length < 16)
        {
            ShowStatus(Loc.Get("CheckNoKey"), StatusKind.Error);
            return;
        }

        try
        {
            _store.SetApiKey(_settings, key);
            _settings.RefreshSeconds = SelectedRefreshSeconds();
            _settings.AlwaysOnTop = AlwaysOnTopToggle.IsChecked == true;
            _settings.StartWithWindows = StartupToggle.IsChecked == true;
            _settings.HideInFullscreen = FullscreenToggle.IsChecked == true;
            _settings.NotifyOnLimits = NotifyToggle.IsChecked == true;
            _settings.NotifySound = SoundToggle.IsChecked == true;
            _settings.NotifySoundId = SelectedSoundId();
            _settings.AnimateOnLimit = AnimateToggle.IsChecked == true;
            _settings.Language = Loc.Language;

            _store.Save(_settings);
            StartupManager.SetEnabled(_settings.StartWithWindows);

            DialogResult = true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowStatus(Loc.Get("SaveError"), StatusKind.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Language_Changed(object sender, RoutedEventArgs e)
    {
        if (!_languageReady)
            return;

        Loc.Apply((sender as RadioButton)?.Tag as string);
        RevealButton.Content = Loc.Get(_revealed ? "Hide" : "Show");
        StatusHost.Visibility = Visibility.Collapsed;
        ImportStatus.Text = "";
    }

    private void SetBusy(bool busy)
    {
        TestButton.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy;
        ImportButton.IsEnabled = !busy;
    }

    private enum StatusKind
    {
        Neutral,
        Success,
        Error,
    }

    private void ShowStatus(string text, StatusKind kind)
    {
        StatusHost.Visibility = Visibility.Visible;
        StatusText.Text = text;

        switch (kind)
        {
            case StatusKind.Success:
                StatusText.Foreground = (Brush)FindResource("SuccessBrush");
                StatusHost.Background = new SolidColorBrush(Color.FromArgb(31, 86, 210, 120));
                break;
            case StatusKind.Error:
                StatusText.Foreground = (Brush)FindResource("DangerBrush");
                StatusHost.Background = new SolidColorBrush(Color.FromArgb(31, 255, 107, 107));
                break;
            default:
                StatusText.Foreground = (Brush)FindResource("SecondaryBrush");
                StatusHost.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
                break;
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

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
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
}
