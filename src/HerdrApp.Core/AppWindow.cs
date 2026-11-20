using System.Diagnostics;
using System.Text.Json;

namespace HerdrApp;

public sealed record Config(bool ActivateApp = true, string AppBundleId = Config.DefaultBundleId)
{
    public const string DefaultBundleId = "dev.herdr.app";

    public static readonly string DefaultPath =
        Environment.GetEnvironmentVariable("HERDR_APP_LOGI_CONFIG")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config/herdr-app-logi-plugin/config.json");

    // ActivateApp brings the Herdr window to the front when a key acts on it.
    // AppBundleId names the app to raise, for a differently built copy.
    public static Config Load(string? path = null)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path ?? DefaultPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new Config();
            var activate = !root.TryGetProperty("activateApp", out var flag) || flag.ValueKind != JsonValueKind.False;
            var bundle = root.TryGetProperty("appBundleId", out var id) && id.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(id.GetString()) ? id.GetString()! : DefaultBundleId;
            return new Config(activate, bundle);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Config();
        }
    }
}

public class AppWindow
{
    // Never throws: a window that cannot be raised must not stop the command
    // that follows it.
    public async Task ActivateAsync()
    {
        try
        {
            var config = Config.Load();
            if (!config.ActivateApp || !OperatingSystem.IsMacOS()) return;
            await RunAsync("/usr/bin/open", "-b", config.AppBundleId);
        }
        catch (Exception e)
        {
            Log.Warning($"could not raise the Herdr app: {e.Message}");
        }
    }

    protected virtual async Task RunAsync(string command, params string[] args)
    {
        var start = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{command} did not start");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var diagnostic = await error;
        await output;
        if (process.ExitCode != 0) throw new InvalidOperationException($"{command} exited with {process.ExitCode}: {diagnostic.Trim()}");
    }
}
