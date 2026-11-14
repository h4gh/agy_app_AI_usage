using System.IO;
using System.Text.Json;

namespace AiUsageMonitor.Services;

public sealed class AppSettings
{
    public bool ShowAntigravity { get; set; } = true;
    public bool ShowClaude { get; set; } = true;
    public int RefreshSeconds { get; set; } = 60;
}

public static class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiUsageMonitor");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            if (!settings.ShowAntigravity && !settings.ShowClaude)
                settings.ShowAntigravity = true;
            if (settings.RefreshSeconds is not (30 or 60 or 120 or 300))
                settings.RefreshSeconds = 60;
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Ignore settings persistence failures
        }
    }
}
