using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.Plug;
using GBX.NET.Engines.Scene;

namespace TmfTrackConverter.Core;

public sealed class BlockMeshResolver
{
    private readonly GameAssetStore _store;
    private readonly Dictionary<string, CompiledMesh> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> MissingBlocks => _missing;

    public BlockMeshResolver(GameAssetStore store)
    {
        _store = store;
    }

    public CompiledMesh? Resolve(CGameCtnBlock block, Int3 blockSize)
    {
        return Resolve(block.Name, block.Variant, block.IsGround, blockSize);
    }

    public CompiledMesh? Resolve(string name, byte variant, bool isGround, Int3 blockSize)
    {
        var key = $"{name}|{variant}|{(isGround ? "G" : "A")}";
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached.IsEmpty ? null : cached;
        }

        var path = _store.FindBlockInfo(name);
        if (path is null)
        {
            _missing.Add(name);
            _cache[key] = new CompiledMesh { Key = key };
            return null;
        }

        return BakeBlockInfo(key, name, path, variant, isGround, blockSize);
    }

    public CompiledMesh? ResolveFlat(string path, Int3 blockSize)
    {
        var stem = GameAssetStore.FlatStem(path);
        var key = $"{stem}|0|G";
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached.IsEmpty ? null : cached;
        }

        return BakeBlockInfo(key, stem, path, 0, true, blockSize);
    }

    private CompiledMesh? BakeBlockInfo(
        string key,
        string name,
        string path,
        byte variant,
        bool isGround,
        Int3 blockSize)
    {
        var info = _store.ParseNode<CGameCtnBlockInfo>(path);
        if (info is null)
        {
            _missing.Add(name);
            _cache[key] = new CompiledMesh { Key = key };
            return null;
        }

        var units = PickUnits(info, isGround).ToList();
        var hasUnderground = units.Any(IsUnderground);
        var mobils = PickMobils(info, isGround, variant).ToList();
        var solids = new List<CompiledMesh>();
        var index = 0;
        foreach (var mobil in mobils)
        {
            var baked = BakeMobil(mobil, $"{key}#{index++}");
            if (baked is null || baked.IsEmpty)
            {
                continue;
            }

            if (hasUnderground && IsUntexturedHelper(baked))
            {
                continue;
            }

            solids.Add(baked);
        }

        var combined = MeshBaker.Combine(key, solids, recenter: false);
        combined.IsFlat = GameAssetStore.IsFlatBlockInfo(path);
        combined.UnitOffsets = units.Select(unit => unit.RelativeOffset).ToList();
        combined.FootprintCells = FootprintCells(combined.UnitOffsets);
        if (combined.IsFlat)
        {
            MeshBaker.ApplyPlanarXzUvs(combined, blockSize.X, blockSize.Z);
        }

        _cache[key] = combined;
        if (combined.IsEmpty)
        {
            _missing.Add(name);
            return null;
        }

        return combined;
    }

    private IEnumerable<CSceneMobil> PickMobils(CGameCtnBlockInfo info, bool isGround, byte variant)
    {
        var primary = isGround ? info.GroundMobils : info.AirMobils;
        var secondary = isGround ? info.AirMobils : info.GroundMobils;
        var fromPrimary = ResolveRow(primary, variant).ToList();
        return fromPrimary.Count > 0 ? fromPrimary : ResolveRow(secondary, variant);
    }

    private static IEnumerable<CGameCtnBlockUnitInfo> PickUnits(CGameCtnBlockInfo info, bool isGround)
    {
        var primary = isGround ? info.GroundBlockUnitInfos : info.AirBlockUnitInfos;
        var secondary = isGround ? info.AirBlockUnitInfos : info.GroundBlockUnitInfos;
        if (primary is { Length: > 0 })
        {
            return primary.Where(unit => unit is not null)!;
        }

        return secondary?.Where(unit => unit is not null) ?? [];
    }

    private IEnumerable<CSceneMobil> ResolveRow(External<CSceneMobil>[][]? sets, byte variant)
    {
        if (sets is null || sets.Length == 0)
        {
            yield break;
        }

        var index = Math.Clamp((int)variant, 0, sets.Length - 1);
        foreach (var external in sets[index] ?? [])
        {
            var mobil = _store.Resolve(external);
            if (mobil is not null)
            {
                yield return mobil;
            }
        }
    }

    private CompiledMesh? BakeMobil(CSceneMobil mobil, string key)
    {
        var item = mobil.Item;
        if (item?.Solid is not CPlugSolid solid)
        {
            return null;
        }

        return MeshBaker.BakeSolid(solid, key, _store);
    }

    private static Int3 FootprintCells(IReadOnlyList<Int3> offsets)
    {
        if (offsets.Count == 0)
        {
            return new Int3(1, 1, 1);
        }

        var maxX = 0;
        var maxZ = 0;
        foreach (var offset in offsets)
        {
            maxX = Math.Max(maxX, offset.X);
            maxZ = Math.Max(maxZ, offset.Z);
        }

        return new Int3(Math.Max(1, maxX + 1), 1, Math.Max(1, maxZ + 1));
    }

    private static bool IsUnderground(CGameCtnBlockUnitInfo unit) => unit.Underground;

    private static bool IsUntexturedHelper(CompiledMesh mesh)
    {
        var vertices = mesh.Parts.Sum(part => part.Vertices.Count);
        var untextured = mesh.Parts.All(part =>
            string.IsNullOrWhiteSpace(part.TextureHint)
            || string.Equals(part.MaterialName, "Unknown", StringComparison.OrdinalIgnoreCase));
        return untextured && vertices <= 48;
    }
}
