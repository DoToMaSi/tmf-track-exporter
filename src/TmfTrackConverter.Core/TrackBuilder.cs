using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;
using GBX.NET.Engines.Scene;

namespace TmfTrackConverter.Core;

public sealed class TrackBuilder
{
    private readonly GameAssetStore _store;
    private readonly AppSettings _settings;

    public TrackBuilder(GameAssetStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
    }

    public TrackScene Build(LoadedMap map, IProgress<TrackBuildProgress>? progress = null)
    {
        var scene = new TrackScene { Map = map };
        var resolver = new BlockMeshResolver(_store);
        var blocks = map.Challenge.GetBlocks().ToList();
        var unique = blocks.Select(b => b.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var processed = 0;
        var pending = new List<(CGameCtnBlock Block, CompiledMesh Mesh)>();
        var presentFlats = new List<string>();

        foreach (var block in blocks)
        {
            processed++;
            if (processed == 1 || processed % 20 == 0)
            {
                progress?.Report(new TrackBuildProgress($"Resolving {block.Name}…", processed, blocks.Count));
            }

            if (_settings.SkipClipBlocks && block.IsClip)
            {
                continue;
            }

            var mesh = resolver.Resolve(block, map.BlockSize);
            if (mesh is null)
            {
                continue;
            }

            pending.Add((block, mesh));
            if (mesh.IsFlat)
            {
                presentFlats.Add(block.Name);
            }
        }

        var construction = new HashSet<(int X, int Z)>();
        foreach (var (block, mesh) in pending)
        {
            if (mesh.IsFlat)
            {
                continue;
            }

            foreach (var cell in BlockPlacement.OccupiedCells(block.Coord, block.Direction, mesh.UnitOffsets))
            {
                construction.Add((cell.X, cell.Z));
            }
        }

        foreach (var (block, mesh) in pending)
        {
            if (mesh.IsFlat)
            {
                var occupied = BlockPlacement.OccupiedCells(block.Coord, block.Direction, mesh.UnitOffsets);
                if (occupied.Any(cell => construction.Contains((cell.X, cell.Z))))
                {
                    continue;
                }
            }

            AddBlockInstance(scene, map, block, mesh);
        }

        foreach (var missing in resolver.MissingBlocks.OrderBy(x => x))
        {
            scene.Warnings.Add($"Missing block mesh: {missing}");
        }

        progress?.Report(new TrackBuildProgress("Loading environment decoration…", blocks.Count, blocks.Count + 2));
        TryAddDecoration(map, scene);

        progress?.Report(new TrackBuildProgress("Filling default cover…", blocks.Count + 1, blocks.Count + 2));
        TryAddDefaultCover(map, scene, resolver, presentFlats);

        progress?.Report(new TrackBuildProgress(
            $"Ready — {scene.Instances.Count} instances, {scene.Meshes.Count} unique meshes, {unique} block types.",
            blocks.Count,
            blocks.Count));

        return scene;
    }

    private static void AddBlockInstance(TrackScene scene, LoadedMap map, CGameCtnBlock block, CompiledMesh mesh)
    {
        scene.Meshes.TryAdd(mesh.Key, mesh);
        var occupied = BlockPlacement.OccupiedCells(block.Coord, block.Direction, mesh.UnitOffsets);
        scene.Instances.Add(new TrackInstance
        {
            Name = $"{block.Name}_{block.Coord.X}_{block.Coord.Y}_{block.Coord.Z}_{block.Direction}",
            MeshKey = mesh.Key,
            Position = BlockPlacement.InstancePosition(
                block.Coord, map.BlockSize, block.Direction, mesh.LocalOrigin),
            YawDegrees = BlockPlacement.YawDegrees(block.Direction),
            BlockName = block.Name,
            Coord = block.Coord,
            Direction = block.Direction,
            IsGround = block.IsGround,
            OccupiedCells = occupied
        });
    }

    private void TryAddDefaultCover(
        LoadedMap map,
        TrackScene scene,
        BlockMeshResolver resolver,
        IReadOnlyList<string> presentFlats)
    {
        var coverPath = _store.FindDefaultCoverFlat(map.Environment, presentFlats);
        if (coverPath is null)
        {
            scene.Warnings.Add("Default base cover Flat was not found for this environment.");
            return;
        }

        var mesh = resolver.ResolveFlat(coverPath, map.BlockSize);
        if (mesh is null)
        {
            scene.Warnings.Add($"Default base cover '{GameAssetStore.FlatStem(coverPath)}' could not be baked.");
            return;
        }

        scene.Meshes.TryAdd(mesh.Key, mesh);
        var occupied = new HashSet<(int X, int Z)>();
        foreach (var instance in scene.Instances)
        {
            if (string.Equals(instance.BlockName, "Environment", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var cell in instance.OccupiedCells)
            {
                occupied.Add((cell.X, cell.Z));
            }
        }

        var stem = GameAssetStore.FlatStem(coverPath);
        var added = 0;
        for (var x = 0; x < map.Size.X; x++)
        {
            for (var z = 0; z < map.Size.Z; z++)
            {
                if (!occupied.Add((x, z)))
                {
                    continue;
                }

                var coord = new Int3(x, 0, z);
                scene.Instances.Add(new TrackInstance
                {
                    Name = $"{stem}_{x}_0_{z}_{Direction.North}",
                    MeshKey = mesh.Key,
                    Position = BlockPlacement.InstancePosition(
                        coord, map.BlockSize, Direction.North, mesh.LocalOrigin),
                    YawDegrees = 0,
                    BlockName = stem,
                    Coord = coord,
                    Direction = Direction.North,
                    IsGround = true,
                    OccupiedCells = [coord]
                });
                added++;
            }
        }

        if (added > 0)
        {
            scene.Warnings.Add($"Filled {added} empty cells with {stem}.");
        }
    }

    private void TryAddDecoration(LoadedMap map, TrackScene scene)
    {
        if (string.IsNullOrWhiteSpace(map.DecorationId))
        {
            return;
        }

        try
        {
            var path = _store.FindDecoration(map.DecorationId, map.Environment, map.Size);
            if (path is null)
            {
                scene.Warnings.Add($"Environment decoration '{map.DecorationId}' was not found in the cache.");
                return;
            }

            var node = _store.ParseNode(path);
            if (node is not CGameCtnDecoration decoration)
            {
                scene.Warnings.Add(
                    $"Could not parse the environment decoration file ({Path.GetFileName(path)} is {node?.GetType().Name ?? "unreadable"}).");
                return;
            }

            scene.TerrainRemapFolder = ReadRemapFolder(decoration);

            var layout = ResolveLayout(decoration);
            Gbx? sceneGbx = null;
            if (layout is null && _store.Install is not null)
            {
                try
                {
                    sceneGbx = PakDecryptor.TryOpenScene3dAsync(
                        _store.Install,
                        map.Environment.PakName(),
                        map.Size).GetAwaiter().GetResult();
                    layout = sceneGbx?.Node as CSceneLayout;
                }
                catch (Exception ex)
                {
                    scene.Warnings.Add($"Could not open the decoration scene from the game pack: {ex.Message}");
                }
            }

            var solids = new List<CompiledMesh>();
            if (layout is not null)
            {
                foreach (var baked in MeshBaker.BakeEach(_store, layout, "deco#layout"))
                {
                    if (!baked.IsEmpty)
                    {
                        solids.Add(baked);
                    }
                }
            }

            foreach (var baked in BakeSceneRefs(decoration, sceneGbx, "deco#ref"))
            {
                solids.Add(baked);
            }

            var warp = ReadWarp(decoration);
            if (warp is not null)
            {
                var baked = MeshBaker.BakeAny(_store, warp, "deco#warp");
                if (baked is not null && !baked.IsEmpty)
                {
                    solids.Add(baked);
                }
            }

            if (solids.Count == 0)
            {
                scene.Warnings.Add(layout is null
                    ? "Decoration scene is not available (best-effort). Blocks were still exported."
                    : "Decoration contained no usable meshes.");
                return;
            }

            var mesh = MeshBaker.Combine("Environment", solids);
            scene.Meshes[mesh.Key] = mesh;
            scene.Instances.Add(new TrackInstance
            {
                Name = "Environment",
                MeshKey = mesh.Key,
                Position = mesh.LocalOrigin,
                YawDegrees = 0,
                BlockName = "Environment"
            });
        }
        catch (Exception ex)
        {
            scene.Warnings.Add($"Environment mesh skipped: {ex.Message}");
        }
    }

    private CSceneLayout? ResolveLayout(CGameCtnDecoration decoration)
    {
        var decoSize = _store.Resolve(decoration.DecoSizeFile, decoration.DecoSize);
        if (decoSize is null)
        {
            return null;
        }

        return _store.Resolve(decoSize.SceneFile, decoSize.Scene);
    }

    private IEnumerable<CompiledMesh> BakeSceneRefs(CGameCtnDecoration decoration, Gbx? sceneGbx, string key)
    {
        var refs = sceneGbx?.RefTable?.Files.ToList() ?? [];
        if (refs.Count == 0)
        {
            var decoSize = _store.Resolve(decoration.DecoSizeFile, decoration.DecoSize);
            var scenePath = decoSize?.SceneFile is { } file ? _store.Find(file.FilePath) : null;
            if (scenePath is not null && File.Exists(scenePath) && new FileInfo(scenePath).Length > 0)
            {
                try
                {
                    refs.AddRange(Gbx.ParseHeader(scenePath).RefTable?.Files ?? []);
                }
                catch
                {
                    // Best-effort only.
                }
            }
        }

        var i = 0;
        foreach (var refFile in refs)
        {
            if (!LooksLikeMesh(refFile.FilePath))
            {
                continue;
            }

            var path = _store.Find(refFile.FilePath);
            if (path is null)
            {
                continue;
            }

            var node = _store.ParseNode(path);
            var baked = MeshBaker.BakeAny(_store, node, $"{key}#{Path.GetFileName(refFile.FilePath)}#{i++}");
            if (baked is not null && !baked.IsEmpty)
            {
                yield return baked;
            }
        }
    }

    private static bool LooksLikeMesh(string path)
    {
        return path.Contains(".Solid", StringComparison.OrdinalIgnoreCase)
               || path.Contains(".Tree", StringComparison.OrdinalIgnoreCase)
               || path.Contains("Scene3d", StringComparison.OrdinalIgnoreCase);
    }

    private string? ReadRemapFolder(CGameCtnDecoration decoration)
    {
        foreach (var name in new[] { "TerrainModifierCovered", "TerrainModifierBase" })
        {
            var modifier = ReadNode<CGameCtnDecorationTerrainModifier>(decoration, name);
            if (!string.IsNullOrWhiteSpace(modifier?.RemapFolder))
            {
                return modifier.RemapFolder;
            }
        }

        return null;
    }

    private CMwNod? ReadWarp(CGameCtnDecoration decoration)
    {
        return ReadNode<CMwNod>(decoration, "DecoratorSolidWarp");
    }

    private T? ReadNode<T>(CMwNod owner, string property) where T : CMwNod
    {
        var type = owner.GetType();
        var nodeProp = type.GetProperty(property);
        if (nodeProp?.GetValue(owner) is T node)
        {
            return node;
        }

        var fileProp = type.GetProperty(property + "File");
        if (fileProp?.GetValue(owner) is GBX.NET.Components.GbxRefTableFile file)
        {
            return _store.Resolve<T>(file);
        }

        var getter = type.GetMethod("Get" + property);
        if (getter?.Invoke(owner, null) is T loaded)
        {
            return loaded;
        }

        return null;
    }
}
