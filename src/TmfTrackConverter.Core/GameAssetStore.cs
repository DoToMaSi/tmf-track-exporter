using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.MwFoundations;

namespace TmfTrackConverter.Core;

public sealed class GameAssetStore
{
    private static readonly string[] BlockInfoSuffixes =
    [
        ".TMEDClassic.Gbx", ".EDClassic.Gbx",
        ".TMEDRoad.Gbx", ".EDRoad.Gbx",
        ".TMEDClip.Gbx", ".EDClip.Gbx",
        ".TMEDFlat.Gbx", ".EDFlat.Gbx",
        ".TMZoneFlat.Gbx", ".ZoneFlat.Gbx",
        ".TMEDFrontier.Gbx", ".EDFrontier.Gbx",
        ".TMZoneFrontier.Gbx", ".ZoneFrontier.Gbx",
        ".TMEDPylon.Gbx", ".EDPylon.Gbx",
        ".TMEDRectAsym.Gbx", ".EDRectAsym.Gbx",
        ".TMEDSlope.Gbx", ".EDSlope.Gbx",
        ".EDTransition.Gbx", ".EDHorizontalClip.Gbx", ".EDVerticalClip.Gbx"
    ];

    private readonly List<string> _roots = [];
    private readonly Dictionary<string, List<string>> _byFileName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _byRelative = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string>? _blockInfoByIdent;
    private Dictionary<string, string>? _flatInfoByIdent;

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
        foreach (var candidate in BlockNameCandidates(blockName))
        {
            foreach (var suffix in BlockInfoSuffixes)
            {
                var found = Find(candidate + suffix);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        EnsureBlockInfoIndex();
        foreach (var candidate in BlockNameCandidates(blockName))
        {
            if (_blockInfoByIdent!.TryGetValue(candidate, out var byIdent))
            {
                return byIdent;
            }
        }

        return FindBlockInfoByPrefix(blockName);
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

        EnsureBlockInfoIndex();
        foreach (var candidate in BlockNameCandidates(blockName))
        {
            if (_flatInfoByIdent!.TryGetValue(candidate, out var byIdent))
            {
                return byIdent;
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
                if (CoverScore(stem) <= 0)
                {
                    continue;
                }

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
        var bestScore = int.MinValue;
        foreach (var (name, files) in _byFileName)
        {
            if (!IsFlatBlockInfo(name))
            {
                continue;
            }

            var path = files[0];
            var score = CoverScore(name);
            if (name.Contains(pak, StringComparison.OrdinalIgnoreCase)
                || name.Contains(display, StringComparison.OrdinalIgnoreCase)
                || path.Contains($"\\{pak}\\", StringComparison.OrdinalIgnoreCase))
            {
                score += 8;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = path;
            }
        }

        return bestScore > 0 ? best : null;
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
        yield return $"{pak}Base";
        yield return $"{display}Grass";
        yield return $"{display}Base";
        switch (environment)
        {
            case TmfEnvironment.Stadium:
                yield return "StadiumGrass";
                break;
            case TmfEnvironment.Island:
                yield return "Grass";
                yield return "IslandGrass";
                break;
            case TmfEnvironment.Desert:
                yield return "SpeedBase";
                yield return "Desert";
                yield return "Desert2";
                yield return "Desert3";
                break;
            case TmfEnvironment.Rally:
                yield return "RallyFlat";
                yield return "RallyRockyGrass";
                yield return "ForestGround";
                break;
            case TmfEnvironment.Bay:
                yield return "BayHarbor";
                yield return "BaySea";
                break;
            case TmfEnvironment.Coast:
                yield return "CoastDirt";
                yield return "CoastHarbor";
                break;
            case TmfEnvironment.Alpine:
                yield return "Snow";
                yield return "AlpineSnow";
                yield return "Tundra";
                yield return "AlpineTundra";
                yield return "Ice";
                break;
        }

        yield return $"{pak}Dirt";
        yield return $"{display}Dirt";
        yield return $"{pak}Sand";
        yield return $"{display}Sand";
        yield return $"{pak}Snow";
        yield return $"{display}Snow";
        yield return $"{pak}Ice";
        yield return $"{display}Ice";
        yield return "Grass";
        yield return "Desert";
        yield return "Snow";
    }

    private static int CoverScore(string name)
    {
        if (name.Contains("River", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Sea", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Water", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Lake", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Pool", StringComparison.OrdinalIgnoreCase))
        {
            return -10;
        }

        if (name.Contains("Grass", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Base", StringComparison.OrdinalIgnoreCase))
        {
            return 10;
        }

        if (name.Contains("Dirt", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Sand", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Snow", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Tundra", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Desert", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Harbor", StringComparison.OrdinalIgnoreCase))
        {
            return 6;
        }

        if (name.Contains("Ice", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Rock", StringComparison.OrdinalIgnoreCase))
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

    private void EnsureBlockInfoIndex()
    {
        if (_blockInfoByIdent is not null)
        {
            return;
        }

        GbxBootstrap.EnsureInitialized();
        var blocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flats = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, files) in _byFileName)
        {
            if (!IsBlockInfoFile(name))
            {
                continue;
            }

            var path = Prefer(files, name);
            try
            {
                if (Gbx.ParseHeader(path).Node is not CGameCtnCollector collector)
                {
                    continue;
                }

                var id = collector.Ident.Id;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                RegisterIdent(blocks, id, path);
                if (IsFlatBlockInfo(name))
                {
                    RegisterIdent(flats, id, path);
                }
            }
            catch
            {
                // Header parse is best-effort; filename lookup still covers many blocks.
            }
        }

        _blockInfoByIdent = blocks;
        _flatInfoByIdent = flats;
    }

    private string? FindBlockInfoByPrefix(string blockName)
    {
        if (_blockInfoByIdent is null || blockName.Length < 8)
        {
            return null;
        }

        string? bestId = null;
        foreach (var id in _blockInfoByIdent.Keys)
        {
            if (id.Length < 8 || id.Length >= blockName.Length)
            {
                continue;
            }

            if (!blockName.StartsWith(id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (bestId is null || id.Length > bestId.Length)
            {
                bestId = id;
            }
        }

        return bestId is null ? null : _blockInfoByIdent[bestId];
    }

    private static void RegisterIdent(Dictionary<string, string> map, string id, string path)
    {
        if (!map.TryGetValue(id, out var existing) || BlockInfoRank(path) < BlockInfoRank(existing))
        {
            map[id] = path;
        }
    }

    private static int BlockInfoRank(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Contains(".TMEDFlat.", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".EDFlat.", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (name.Contains(".TMEDClassic.", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".EDClassic.", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (name.Contains(".TMEDRoad.", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".EDRoad.", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (name.Contains(".TMED", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".ED", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (name.Contains(".TMZone", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".Zone", StringComparison.OrdinalIgnoreCase))
        {
            return 10;
        }

        return 5;
    }

    private static bool IsBlockInfoFile(string name)
    {
        if (!name.EndsWith(".Gbx", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.Contains(".TMED", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".TMZone", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".EDClassic.", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".EDRoad.", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".EDClip.", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".EDFlat.", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".EDFrontier.", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".EDPylon.", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".EDRectAsym.", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".EDSlope.", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".EDTransition.", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".EDHorizontalClip.", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".EDVerticalClip.", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".ZoneFlat.", StringComparison.OrdinalIgnoreCase)
               || name.Contains(".ZoneFrontier.", StringComparison.OrdinalIgnoreCase);
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
            return Gbx.ParseNode(path, new GbxReadSettings { IgnoreExceptionsInBody = true });
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

        var found = Find(file.FilePath);
        if (found is not null)
        {
            var parsed = ParseNode<T>(found);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        try
        {
            return file.GetNode<T>(ref existing);
        }
        catch
        {
            return null;
        }
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
