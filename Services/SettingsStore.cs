using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenCodeWidget.Models;

namespace OpenCodeWidget.Services;

public sealed class SettingsStore
{
    private const string AppFolder = "OpenCodeWidget";
    private const string SettingsFileName = "settings.json";
    private const string SnapshotFileName = "last-snapshot.json";

    public string DataDirectory { get; }
    public string SettingsPath { get; }
    public string SnapshotPath { get; }

    public SettingsStore()
    {
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolder);
        SettingsPath = Path.Combine(DataDirectory, SettingsFileName);
        SnapshotPath = Path.Combine(DataDirectory, SnapshotFileName);
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, GoJson.Options);
            if (settings is null)
                return new AppSettings();

            settings.RefreshSeconds = Math.Clamp(settings.RefreshSeconds, 30, 3600);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });

        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, json, Encoding.UTF8);
        File.Move(temp, SettingsPath, true);
    }

    public string? GetApiKey(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ProtectedApiKey))
            return null;

        try
        {
            var bytes = Convert.FromBase64String(settings.ProtectedApiKey);
            var plain = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            var key = Encoding.UTF8.GetString(plain).Trim();
            return key.Length > 0 ? key : null;
        }
        catch
        {
            return null;
        }
    }

    public void SetApiKey(AppSettings settings, string key)
    {
        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(key.Trim()),
            null,
            DataProtectionScope.CurrentUser);
        settings.ProtectedApiKey = Convert.ToBase64String(bytes);
    }

    public GoSnapshot? LoadSnapshot()
    {
        try
        {
            if (!File.Exists(SnapshotPath))
                return null;

            var json = File.ReadAllText(SnapshotPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<GoSnapshot>(json, GoJson.Options);
        }
        catch
        {
            return null;
        }
    }

    public void SaveSnapshot(GoSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var json = JsonSerializer.Serialize(snapshot, GoJson.Options);
            File.WriteAllText(SnapshotPath, json, Encoding.UTF8);
        }
        catch
        {
            // Cache is best-effort only.
        }
    }

    public void ClearSnapshot()
    {
        try
        {
            if (File.Exists(SnapshotPath))
                File.Delete(SnapshotPath);
        }
        catch
        {
            // Ignore.
        }
    }
}
