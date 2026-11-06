using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace OpenCodeWidget.Services;

public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OpenCodeWidget";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (key is null)
                return;

            if (enabled)
            {
                var command = BuildCommand();
                if (!string.IsNullOrWhiteSpace(command))
                    key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch
        {
            // Registry may be blocked by policy; the toggle simply will not persist.
        }
    }

    private static string BuildCommand()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            return "";

        if (Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            var assemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
            var assemblyPath = string.IsNullOrWhiteSpace(assemblyName)
                ? null
                : Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");

            return string.IsNullOrWhiteSpace(assemblyPath)
                ? ""
                : $"\"{processPath}\" \"{assemblyPath}\" --autostart";
        }

        return $"\"{processPath}\" --autostart";
    }
}
