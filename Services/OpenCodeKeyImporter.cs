using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OpenCodeWidget.Services;

public static class OpenCodeKeyImporter
{
    private const string Query =
        "select value from credential where integration_id='opencode-go' order by time_updated desc limit 1";

    public static async Task<string?> TryImportAsync(CancellationToken cancellationToken = default)
    {
        var executable = FindOpenCodeExecutable();
        if (executable is null)
            return null;

        try
        {
            var output = await RunQueryAsync(executable, cancellationToken).ConfigureAwait(false);
            return ParseKey(output);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindOpenCodeExecutable()
    {
        var candidates = new List<string>();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        candidates.Add(Path.Combine(appData, "npm", "node_modules", "opencode-ai", "bin", "opencode.exe"));
        candidates.Add(Path.Combine(appData, "npm", "node_modules", "opencode-ai", "node_modules",
            "opencode-windows-x64", "bin", "opencode.exe"));
        candidates.Add(Path.Combine(appData, "npm", "opencode.exe"));
        candidates.Add(Path.Combine(localAppData, "Programs", "opencode", "opencode.exe"));

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                candidates.Add(Path.Combine(directory.Trim(), "opencode.exe"));
                candidates.Add(Path.Combine(directory.Trim(), "opencode.cmd"));
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        candidates.Add(Path.Combine(appData, "npm", "opencode.cmd"));

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<string> RunQueryAsync(string executable, CancellationToken cancellationToken)
    {
        var isCmd = executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (isCmd)
        {
            var comspec = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            startInfo.FileName = comspec;
            startInfo.Arguments = $"/d /s /c \"\"{executable}\" db \"{Query}\" --format json\"";
        }
        else
        {
            startInfo.FileName = executable;
            startInfo.ArgumentList.Add("db");
            startInfo.ArgumentList.Add(Query);
            startInfo.ArgumentList.Add("--format");
            startInfo.ArgumentList.Add("json");
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            return "";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
                // Ignore.
            }

            return "";
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        _ = await stderrTask.ConfigureAwait(false);
        return stdout;
    }

    private static string? ParseKey(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var start = output.IndexOf('[');
        var end = output.LastIndexOf(']');
        if (start < 0 || end <= start)
            return null;

        try
        {
            using var document = JsonDocument.Parse(output[start..(end + 1)]);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (!row.TryGetProperty("value", out var valueElement))
                    continue;

                var raw = valueElement.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var key = ExtractKey(raw);
                if (key is not null)
                    return key;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string? ExtractKey(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith("oc_sk_", StringComparison.Ordinal) && raw.Length > 20)
            return raw;

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("key", out var keyElement))
            {
                var key = keyElement.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(key) && key.StartsWith("oc_sk_", StringComparison.Ordinal))
                    return key;
            }
        }
        catch
        {
            // Not JSON.
        }

        return null;
    }
}
