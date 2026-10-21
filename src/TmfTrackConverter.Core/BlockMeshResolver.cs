using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.Hms;
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

    public CompiledMesh? Resolve(CGameCtnBlock block)
    {
        var key = $"{block.Name}|{block.Variant}|{(block.IsGround ? "G" : "A")}";
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached.IsEmpty ? null : cached;
        }

        var path = _store.FindBlockInfo(block.Name);
        if (path is null)
        {
            _missing.Add(block.Name);
            _cache[key] = new CompiledMesh { Key = key };
            return null;
        }

        var info = _store.ParseNode<CGameCtnBlockInfo>(path);
        if (info is null)
        {
            _missing.Add(block.Name);
            _cache[key] = new CompiledMesh { Key = key };
            return null;
        }

        var mobils = PickMobils(info, block.IsGround, block.Variant);
        var solids = new List<CompiledMesh>();
        var index = 0;
        foreach (var mobil in mobils)
        {
            var baked = BakeMobil(mobil, $"{key}#{index++}");
            if (baked is not null && !baked.IsEmpty)
            {
                solids.Add(baked);
            }
        }

        var combined = MeshBaker.Combine(key, solids);
        _cache[key] = combined;
        if (combined.IsEmpty)
        {
            _missing.Add(block.Name);
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
}
