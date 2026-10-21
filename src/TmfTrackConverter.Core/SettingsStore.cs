using System.Text.Json;

namespace TmfTrackConverter.Core;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AppSettings Load()
    {
        AppPaths.EnsureRoot();

        if (!File.Exists(AppPaths.SettingsFile))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(AppPaths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        AppPaths.EnsureRoot();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(AppPaths.SettingsFile, json);
    }
}
