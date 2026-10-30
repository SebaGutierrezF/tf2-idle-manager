using System.Text.Json;

namespace TF2IdleManager;

public static class SettingsStore
{
    private static string SettingsPath => Path.Combine(AppLogger.AppDataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings { AutoClass = "Random" };

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            if (string.IsNullOrWhiteSpace(settings.AutoClass))
                settings.AutoClass = settings.AutoSelectHeavy ? "Heavy" : "Random";
            settings.AutoClass = Tf2Classes.Normalize(settings.AutoClass);
            settings.RepeatCount = Math.Clamp(settings.RepeatCount, 1, 100);
            settings.RepeatIntervalSeconds = Math.Clamp(settings.RepeatIntervalSeconds, 5, 3600);
            settings.NotificationCorner = ToastPlacement.Normalize(settings.NotificationCorner);
            settings.ServerProfiles ??= new List<string>();
            return settings;
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not load settings: {ex.Message}");
            return new AppSettings { AutoClass = "Random" };
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppLogger.AppDataDirectory);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Could not save settings: {ex.Message}");
        }
    }
}
