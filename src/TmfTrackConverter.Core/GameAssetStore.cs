using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.MwFoundations;

namespace TmfTrackConverter.Core;

public sealed class GameAssetStore
{
    private readonly List<string> _roots = [];
    private readonly Dictionary<string, List<string>> _byFileName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _byRelative = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Roots => _roots;

    public GameInstall? Install { get; private set; }

    public static GameAssetStore Open(GameInstall install)
    {
        var store = new GameAssetStore { Install = install };
        if (Directory.Exists(install.GameDataFolder))
        {
            store.AddRoot(install.GameDataFolder);
        }

        if (Directory.Exists(AppPaths.CacheRoot))
        {
            foreach (var packDir in Directory.GetDirectories(AppPaths.CacheRoot))
            {
                store.AddRoot(packDir);
            }

            store.AddRoot(AppPaths.CacheRoot);
        }

        return store;
    }

    public void AddRoot(string root)
    {
        if (!Directory.Exists(root) || _roots.Any(r => string.Equals(r, root, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _roots.Add(root);

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (!_byFileName.TryGetValue(name, out var list))
            {
                list = [];
                _byFileName[name] = list;
            }

            list.Add(file);

            var relative = Path.GetRelativePath(root, file);
            _byRelative.TryAdd(relative.Replace('/', '\\'), file);

            var gameDataIndex = relative.IndexOf("GameData", StringComparison.OrdinalIgnoreCase);
            if (gameDataIndex >= 0)
            {
                _byRelative.TryAdd(relative[gameDataIndex..].Replace('/', '\\'), file);
            }
        }
    }

    public string? Find(string? pathOrName)
    {
        if (string.IsNullOrWhiteSpace(pathOrName))
        {
            return null;
        }

        var normalized = pathOrName.Replace('/', '\\').TrimStart('\\');

        if (File.Exists(pathOrName))
        {
            return pathOrName;
        }

        foreach (var root in _roots)
        {
            var candidate = Path.Combine(root, normalized);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        if (_byRelative.TryGetValue(normalized, out var byRel))
        {
            return byRel;
        }

        var fileName = Path.GetFileName(normalized);
        if (_byFileName.TryGetValue(fileName, out var matches))
        {
            return Prefer(matches, normalized);
        }

        var stem = GbxPath.GetFileNameWithoutExtension(fileName) ?? Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrEmpty(stem))
        {
            return null;
        }

        var suffix = fileName.Length > stem.Length ? fileName[stem.Length..] : string.Empty;
        foreach (var (name, files) in _byFileName)
        {
            var otherStem = GbxPath.GetFileNameWithoutExtension(name) ?? Path.GetFileNameWithoutExtension(name);
            if (!string.Equals(otherStem, stem, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (suffix.Length > 0 && !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Prefer(files, normalized);
        }

        return null;
    }

    public IEnumerable<string> FindAll(string fileName)
    {
        return _byFileName.TryGetValue(fileName, out var files) ? files : [];
    }

    public string? FindBlockInfo(string blockName)
    {
        string[] suffixes =
        [
            ".TMEDClassic.Gbx", ".EDClassic.Gbx",
            ".TMEDRoad.Gbx", ".EDRoad.Gbx",
            ".TMEDClip.Gbx", ".EDClip.Gbx",
            ".TMEDFlat.Gbx", ".EDFlat.Gbx",
            ".TMZoneFlat.Gbx", ".ZoneFlat.Gbx",
            ".TMEDFrontier.Gbx", ".EDFrontier.Gbx",
            ".TMEDPylon.Gbx", ".EDPylon.Gbx",
            ".TMEDRectAsym.Gbx", ".EDRectAsym.Gbx",
            ".EDTransition.Gbx", ".EDHorizontalClip.Gbx", ".EDVerticalClip.Gbx"
        ];

        foreach (var candidate in BlockNameCandidates(blockName))
        {
            foreach (var suffix in suffixes)
            {
                var found = Find(candidate + suffix);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        foreach (var candidate in BlockNameCandidates(blockName))
        {
            foreach (var (name, files) in _byFileName)
            {
                var stem = GbxPath.GetFileNameWithoutExtension(name);
                if (string.Equals(stem, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return Prefer(files, candidate);
                }
            }
        }

        return null;
    }

    public string? FindFlatBlockInfo(string blockName)
    {
        string[] suffixes = [".TMEDFlat.Gbx", ".EDFlat.Gbx", ".TMZoneFlat.Gbx", ".ZoneFlat.Gbx"];
        foreach (var candidate in BlockNameCandidates(blockName))
        {
            foreach (var suffix in suffixes)
            {
                var found = Find(candidate + suffix);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    public string? FindDefaultCoverFlat(TmfEnvironment environment, IEnumerable<string>? presentFlatStems = null)
    {
        foreach (var stem in DefaultCoverStems(environment))
        {
            var found = FindFlatBlockInfo(stem);
            if (found is not null)
            {
                return found;
            }
        }

        if (presentFlatStems is not null)
        {
            foreach (var stem in presentFlatStems.Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderByDescending(name => CoverScore(name)))
            {
                var found = FindFlatBlockInfo(stem);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        var pak = environment.PakName();
        var display = environment.ToString();
        string? best = null;
        var bestScore = 0;
        foreach (var (name, files) in _byFileName)
        {
            if (!IsFlatBlockInfo(name))
            {
                continue;
            }

            var score = CoverScore(name);
            if (name.Contains(pak, StringComparison.OrdinalIgnoreCase)
                || name.Contains(display, StringComparison.OrdinalIgnoreCase))
            {
                score += 4;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = files[0];
            }
        }

        return best;
    }

    public static bool IsFlatBlockInfo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var name = Path.GetFileName(path);
        return name.Contains(".TMEDFlat.", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".EDFlat.", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".TMZoneFlat.", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".ZoneFlat.", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".TMEDFlat.Gbx", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".EDFlat.Gbx", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".TMZoneFlat.Gbx", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".ZoneFlat.Gbx", StringComparison.OrdinalIgnoreCase);
    }

    public static string FlatStem(string path)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(".Gbx", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        foreach (var suffix in new[] { ".TMEDFlat", ".EDFlat", ".TMZoneFlat", ".ZoneFlat" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^suffix.Length];
            }
        }

        return GbxPath.GetFileNameWithoutExtension(Path.GetFileName(path))
               ?? Path.GetFileNameWithoutExtension(path)
               ?? path;
    }

    public static bool IsBaseTerrain(string blockName, string? blockInfoPath)
    {
        return IsFlatBlockInfo(blockInfoPath)
               && string.Equals(blockName, FlatStem(blockInfoPath!), StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> DefaultCoverStems(TmfEnvironment environment)
    {
        var pak = environment.PakName();
        var display = environment.ToString();
        yield return $"{pak}Grass";
        yield return $"{display}Grass";
        yield return $"{pak}Dirt";
        yield return $"{display}Dirt";
        yield return $"{pak}Sand";
        yield return $"{display}Sand";
        yield return $"{pak}Snow";
        yield return $"{display}Snow";
        yield return $"{pak}Ice";
        yield return $"{display}Ice";
    }

    private static int CoverScore(string name)
    {
        if (name.Contains("Grass", StringComparison.OrdinalIgnoreCase))
        {
            return 10;
        }

        if (name.Contains("Dirt", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Sand", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Snow", StringComparison.OrdinalIgnoreCase))
        {
            return 6;
        }

        if (name.Contains("Ice", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        return 1;
    }

    private static IEnumerable<string> BlockNameCandidates(string blockName)
    {
        yield return blockName;

        if (blockName.EndsWith("Line", StringComparison.OrdinalIgnoreCase))
        {
            yield return blockName[..^4];
        }

        if (blockName.Contains("StartFinish", StringComparison.OrdinalIgnoreCase))
        {
            yield return blockName.Replace("StartFinishLine", "Start", StringComparison.OrdinalIgnoreCase);
            yield return blockName.Replace("StartFinish", "Start", StringComparison.OrdinalIgnoreCase);
            yield return blockName.Replace("StartFinishLine", "Finish", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string? FindDecoration(string decorationId, TmfEnvironment? environment = null, Int3? size = null)
    {
        var mood = ToDayMood(decorationId);
        var pak = environment?.PakName();
        var sizeHint = size is { } s ? $"{s.X}x{s.Z}" : null;

        var names = new List<string>();
        if (pak is not null && sizeHint is not null)
        {
            names.Add($"{pak}Base{sizeHint}{mood}.TMDecoration.Gbx");
            names.Add($"{pak}Base{sizeHint}.TMDecoration.Gbx");
            names.Add($"{pak}Base{sizeHint}{decorationId}.TMDecoration.Gbx");
        }

        foreach (var candidate in new[] { mood, decorationId })
        {
            names.Add($"{candidate}.TMDecoration.Gbx");
            names.Add($"{candidate}.Decoration.Gbx");
        }

        foreach (var name in names)
        {
            if (_byFileName.TryGetValue(name, out var files))
            {
                return Prefer(files, name);
            }
        }

        string? best = null;
        var bestScore = 0;
        foreach (var (name, files) in _byFileName)
        {
            if (!IsDecorationFile(name))
            {
                continue;
            }

            var score = 0;
            if (pak is not null && name.Contains(pak, StringComparison.OrdinalIgnoreCase))
            {
                score += 4;
            }

            if (sizeHint is not null && name.Contains(sizeHint, StringComparison.OrdinalIgnoreCase))
            {
                score += 4;
            }

            if (name.Contains(mood, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }
            else if (name.Contains(decorationId, StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = Prefer(files, decorationId);
            }
        }

        return best;
    }

    private static bool IsDecorationFile(string name)
    {
        if (name.Contains("Audio", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Mood", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Size", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.EndsWith(".TMDecoration.Gbx", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".Decoration.Gbx", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToDayMood(string decorationId)
    {
        if (decorationId.Contains("Night", StringComparison.OrdinalIgnoreCase))
        {
            return decorationId.Replace("Night", "Day", StringComparison.OrdinalIgnoreCase);
        }

        if (decorationId.Contains("Sunset", StringComparison.OrdinalIgnoreCase))
        {
            return decorationId.Replace("Sunset", "Day", StringComparison.OrdinalIgnoreCase);
        }

        if (decorationId.Contains("Sunrise", StringComparison.OrdinalIgnoreCase))
        {
            return decorationId.Replace("Sunrise", "Day", StringComparison.OrdinalIgnoreCase);
        }

        return decorationId;
    }

    public T? ParseNode<T>(string path) where T : CMwNod
    {
        return ParseNode(path) as T;
    }

    public CMwNod? ParseNode(string path)
    {
        GbxBootstrap.EnsureInitialized();
        try
        {
            var settings = path.Contains("Scene3d", StringComparison.OrdinalIgnoreCase)
                ? new GbxReadSettings { IgnoreExceptionsInBody = true }
                : default;
            return Gbx.ParseNode(path, settings);
        }
        catch
        {
            return null;
        }
    }

    public T? Resolve<T>(GbxRefTableFile? file, T? existing = null) where T : CMwNod
    {
        if (existing is not null)
        {
            return existing;
        }

        if (file is null)
        {
            return null;
        }

        try
        {
            var loaded = file.GetNode<T>(ref existing);
            if (loaded is not null)
            {
                return loaded;
            }
        }
        catch
        {
            // Fall through to the overlay search.
        }

        var found = Find(file.FilePath);
        return found is null ? null : ParseNode<T>(found);
    }

    public T? Resolve<T>(External<T>? external) where T : CMwNod
    {
        if (external is null)
        {
            return null;
        }

        return Resolve(external.File, external.Node);
    }

    public long CacheSizeBytes()
    {
        if (!Directory.Exists(AppPaths.CacheRoot))
        {
            return 0;
        }

        return Directory.EnumerateFiles(AppPaths.CacheRoot, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
    }

    private static string Prefer(List<string> files, string hint)
    {
        var day = files.FirstOrDefault(f => f.Contains("Day", StringComparison.OrdinalIgnoreCase)
                                           && !f.Contains("Night", StringComparison.OrdinalIgnoreCase));
        if (day is not null && hint.Contains("Day", StringComparison.OrdinalIgnoreCase))
        {
            return day;
        }

        var hinted = files.FirstOrDefault(f => f.Contains(hint, StringComparison.OrdinalIgnoreCase));
        return hinted ?? files[0];
    }
}
