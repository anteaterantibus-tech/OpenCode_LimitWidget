using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using OpenCodeWidget.Services;
using Application = System.Windows.Application;

namespace OpenCodeWidget;

public partial class App : Application
{
    private const string MutexName = @"Local\OpenCodeWidget.SingleInstance";

    private Mutex? _mutex;
    private TrayIconManager? _tray;
    private SettingsStore? _store;
    private AppSettings? _settings;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            args.Handled = true;
        };

        _store = new SettingsStore();
        _settings = _store.Load();

        if (string.IsNullOrWhiteSpace(_store.GetApiKey(_settings)))
        {
            try
            {
                var imported = await OpenCodeKeyImporter.TryImportAsync();
                if (!string.IsNullOrWhiteSpace(imported))
                {
                    _store.SetApiKey(_settings, imported);
                    _store.Save(_settings);
                }
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }

        var window = new MainWindow(_store, _settings);
        MainWindow = window;
        window.Show();

        _tray = new TrayIconManager(window, _store, _settings);
        _tray.Initialize();
    }

    internal static void Log(Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenCodeWidget");
            Directory.CreateDirectory(dir);
            var text = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(dir, "error.log"), text, Encoding.UTF8);
        }
        catch
        {
            // Logging must never bring the widget down.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
