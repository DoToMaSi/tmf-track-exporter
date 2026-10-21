namespace TmfTrackConverter.Core;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TmfTrackConverter");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string CacheRoot => Path.Combine(Root, "cache");

    public static string CacheStaging => Path.Combine(Root, "cache.staging");

    public static string ManifestFile => Path.Combine(CacheRoot, "manifest.json");

    public static void EnsureRoot() => Directory.CreateDirectory(Root);
}
