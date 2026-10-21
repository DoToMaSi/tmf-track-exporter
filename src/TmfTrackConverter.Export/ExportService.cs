using TmfTrackConverter.Core;

namespace TmfTrackConverter.Export;

public sealed class ExportService
{
    public ExportResult Export(
        TrackScene scene,
        GameAssetStore store,
        ExportOptions options,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.FbxPath)!);
        Directory.CreateDirectory(options.TextureDirectory);

        var exportScene = FilterScene(scene, options.IncludeEnvironment);
        progress?.Report(new ExportProgress("Collecting materials…", 0, 1));
        var extractor = new MaterialExtractor(store);
        var sources = extractor.CollectSources(exportScene.Meshes.Values, exportScene.TerrainRemapFolder);

        var textureCount = sources.Count;
        var meshCount = exportScene.Meshes.Count;
        var instanceCount = options.WriteObjSidecar ? exportScene.Instances.Count : 0;
        var total = Math.Max(1, textureCount + meshCount + instanceCount + 1);
        var current = 0;

        try
        {
            foreach (var (material, source) in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new ExportProgress("Exporting textures…", current, total, material));
                var dest = ImageCodec.ExportTexture(source.Path, options.TextureDirectory, material, source.Tint);
                if (dest is not null)
                {
                    exportScene.MaterialTextures[material] = dest;
                }

                current++;
            }

            foreach (var missing in extractor.MissingMaterials.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
            {
                exportScene.Warnings.Add($"Missing texture: {missing}");
                scene.MissingTextures.Add(missing);
            }

            FbxExporter.Write(
                exportScene,
                options.FbxPath,
                (index, key) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new ExportProgress("Writing FBX…", current + index, total, key));
                },
                cancellationToken);

            current += meshCount;
            progress?.Report(new ExportProgress("Writing FBX…", current, total, "Finishing file"));

            string? objPath = null;
            if (options.WriteObjSidecar)
            {
                objPath = Path.ChangeExtension(options.FbxPath, ".obj");
                ObjTrackExporter.Write(
                    exportScene,
                    objPath,
                    (index, name) =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        progress?.Report(new ExportProgress("Writing OBJ sidecar…", current + index, total, name));
                    },
                    cancellationToken);
                current += instanceCount;
            }

            current = total;
            progress?.Report(new ExportProgress("Export finished.", current, total));
            foreach (var (material, path) in exportScene.MaterialTextures)
            {
                scene.MaterialTextures[material] = path;
            }

            return new ExportResult(options.FbxPath, objPath, exportScene.MaterialTextures.Count);
        }
        catch (OperationCanceledException)
        {
            TryDelete(options.FbxPath);
            if (options.WriteObjSidecar)
            {
                TryDelete(Path.ChangeExtension(options.FbxPath, ".obj"));
                TryDelete(Path.ChangeExtension(options.FbxPath, ".mtl"));
            }

            throw;
        }
    }

    private static TrackScene FilterScene(TrackScene scene, bool includeEnvironment)
    {
        if (includeEnvironment)
        {
            return scene;
        }

        var filtered = new TrackScene
        {
            Map = scene.Map,
            TerrainRemapFolder = scene.TerrainRemapFolder
        };
        foreach (var instance in scene.Instances)
        {
            if (string.Equals(instance.BlockName, "Environment", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            filtered.Instances.Add(instance);
            if (scene.Meshes.TryGetValue(instance.MeshKey, out var mesh))
            {
                filtered.Meshes.TryAdd(mesh.Key, mesh);
            }
        }

        return filtered;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Leave the partial file if it is still locked.
        }
    }
}

public sealed record ExportResult(string FbxPath, string? ObjPath, int TextureCount);
