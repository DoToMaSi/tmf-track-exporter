using System.Text.Json;

namespace TmfTrackConverter.Core;

public sealed class CacheFileFingerprint
{
    public string RelativePath { get; set; } = "";

    public long Length { get; set; }

    public long LastWriteUtcTicks { get; set; }
}

public sealed class CacheManifest
{
    public string GameFolder { get; set; } = "";

    public GameEdition Edition { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public List<string> Environments { get; set; } = [];

    public List<CacheFileFingerprint> Fingerprints { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static CacheManifest Create(GameInstall install)
    {
        return new CacheManifest
        {
            GameFolder = install.Folder,
            Edition = install.Edition,
            CreatedUtc = DateTimeOffset.UtcNow,
            Environments = install.AvailableEnvironments.Select(e => e.ToString()).ToList(),
            Fingerprints = CollectFingerprints(install)
        };
    }

    public static CacheManifest? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public bool Matches(GameInstall install)
    {
        if (!string.Equals(GameFolder, install.Folder, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var current = CollectFingerprints(install);
        if (current.Count != Fingerprints.Count)
        {
            return false;
        }

        var lookup = Fingerprints.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        foreach (var file in current)
        {
            if (!lookup.TryGetValue(file.RelativePath, out var previous))
            {
                return false;
            }

            if (previous.Length != file.Length || previous.LastWriteUtcTicks != file.LastWriteUtcTicks)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsReady(GameInstall install)
    {
        var manifest = Load(AppPaths.ManifestFile);
        return manifest is not null && Directory.Exists(AppPaths.CacheRoot) && manifest.Matches(install);
    }

    private static List<CacheFileFingerprint> CollectFingerprints(GameInstall install)
    {
        var files = new List<string> { install.ExecutablePath, install.PackListPath };
        files.AddRange(Directory.GetFiles(install.PacksFolder, "*.pak"));

        return files
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new CacheFileFingerprint
                {
                    RelativePath = Path.GetRelativePath(install.Folder, path),
                    Length = info.Length,
                    LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks
                };
            })
            .ToList();
    }
}
