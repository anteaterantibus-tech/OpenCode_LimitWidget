using System.Drawing;
using WinForms = System.Windows.Forms;

namespace OpenCodeWidget.Services;

public sealed class TrayIconManager : IDisposable
{
    private readonly MainWindow _window;
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;

    private WinForms.NotifyIcon? _notifyIcon;
    private WinForms.ToolStripMenuItem? _normalItem;
    private WinForms.ToolStripMenuItem? _compactItem;
    private WinForms.ToolStripMenuItem? _taskbarItem;
    private WinForms.ToolStripMenuItem? _startupItem;
    private Icon? _icon;

    public TrayIconManager(MainWindow window, SettingsStore store, AppSettings settings)
    {
        _window = window;
        _store = store;
        _settings = settings;
    }

    public void Initialize()
    {
        _icon = CreateIcon();

        var menu = new WinForms.ContextMenuStrip
        {
            BackColor = Color.FromArgb(28, 28, 32),
            ForeColor = Color.FromArgb(244, 244, 246),
            Font = new Font("Segoe UI", 9f),
            ShowImageMargin = false,
            Padding = new WinForms.Padding(4),
            Renderer = new WinForms.ToolStripProfessionalRenderer(new DarkColorTable()),
        };

        var showItem = new WinForms.ToolStripMenuItem("Показать виджет")
        {
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        };
        showItem.Click += (_, _) => OnUi(_window.ShowFromTray);

        var refreshItem = new WinForms.ToolStripMenuItem("Обновить");
        refreshItem.Click += (_, _) => OnUi(() => _ = _window.RefreshAsync());

        var settingsItem = new WinForms.ToolStripMenuItem("Настройки");
        settingsItem.Click += (_, _) => OnUi(_window.OpenSettings);

        _normalItem = new WinForms.ToolStripMenuItem("Обычный вид");
        _normalItem.Click += (_, _) => OnUi(() => _window.SetMode(WidgetMode.Normal));

        _compactItem = new WinForms.ToolStripMenuItem("Компактный вид");
        _compactItem.Click += (_, _) => OnUi(() => _window.SetMode(WidgetMode.Compact));

        _taskbarItem = new WinForms.ToolStripMenuItem("В панели задач");
        _taskbarItem.Click += (_, _) => OnUi(() => _window.SetMode(WidgetMode.Taskbar));

        _startupItem = new WinForms.ToolStripMenuItem("Запускать с Windows")
        {
            Checked = StartupManager.IsEnabled(),
        };
        _startupItem.Click += (_, _) => OnUi(ToggleStartup);

        var exitItem = new WinForms.ToolStripMenuItem("Выход")
        {
            ForeColor = Color.FromArgb(255, 150, 150),
        };
        exitItem.Click += (_, _) => OnUi(_window.RequestExit);

        menu.Items.Add(showItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_normalItem);
        menu.Items.Add(_compactItem);
        menu.Items.Add(_taskbarItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_startupItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        menu.Opening += (_, _) =>
        {
            if (_normalItem is not null)
                _normalItem.Checked = !_settings.CompactMode && !_settings.TaskbarMode;

            if (_compactItem is not null)
                _compactItem.Checked = _settings.CompactMode && !_settings.TaskbarMode;

            if (_taskbarItem is not null)
                _taskbarItem.Checked = _settings.TaskbarMode;

            if (_startupItem is not null)
                _startupItem.Checked = StartupManager.IsEnabled();
        };

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _icon,
            Text = "OpenCode Go - лимиты подписки",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _notifyIcon.DoubleClick += (_, _) => OnUi(_window.ShowFromTray);
    }

    private void ToggleStartup()
    {
        var enabled = !StartupManager.IsEnabled();
        StartupManager.SetEnabled(enabled);

        _settings.StartWithWindows = enabled;
        if (_startupItem is not null)
            _startupItem.Checked = enabled;

        try
        {
            _store.Save(_settings);
        }
        catch
        {
            // Settings persistence is best-effort here.
        }
    }

    private void OnUi(Action action)
    {
        if (_window.Dispatcher.CheckAccess())
            action();
        else
            _window.Dispatcher.BeginInvoke(action);
    }

    private static Icon CreateIcon()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var icon = Icon.ExtractAssociatedIcon(processPath);
                if (icon is not null)
                    return icon;
            }
        }
        catch
        {
            // Fall back to the generic application icon.
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _icon?.Dispose();
        _icon = null;
    }

    private sealed class DarkColorTable : WinForms.ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(28, 28, 32);
        public override Color ImageMarginGradientBegin => Color.FromArgb(28, 28, 32);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(28, 28, 32);
        public override Color ImageMarginGradientEnd => Color.FromArgb(28, 28, 32);
        public override Color MenuBorder => Color.FromArgb(60, 60, 68);
        public override Color MenuItemBorder => Color.FromArgb(60, 60, 68);
        public override Color MenuItemSelected => Color.FromArgb(42, 42, 48);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(42, 42, 48);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(42, 42, 48);
        public override Color SeparatorDark => Color.FromArgb(52, 52, 60);
        public override Color SeparatorLight => Color.FromArgb(36, 36, 41);
        public override Color CheckBackground => Color.FromArgb(66, 245, 197, 66);
        public override Color CheckSelectedBackground => Color.FromArgb(90, 245, 197, 66);
    }
}
