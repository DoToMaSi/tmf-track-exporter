using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;

namespace TmfTrackConverter.Core;

public sealed class MaterialExtractor
{
    private readonly GameAssetStore _store;

    public List<string> MissingMaterials { get; } = [];

    public MaterialExtractor(GameAssetStore store)
    {
        _store = store;
    }

    public Dictionary<string, TextureSource> CollectSources(
        IEnumerable<CompiledMesh> meshes,
        string? terrainRemapFolder = null)
    {
        var sources = new Dictionary<string, TextureSource>(StringComparer.OrdinalIgnoreCase);

        foreach (var mesh in meshes)
        {
            foreach (var part in mesh.Parts)
            {
                if (sources.ContainsKey(part.MaterialName))
                {
                    continue;
                }

                var source = ResolveSource(part, terrainRemapFolder);
                if (source is null)
                {
                    MissingMaterials.Add(part.MaterialName);
                    continue;
                }

                sources[part.MaterialName] = source;
            }
        }

        return sources;
    }

    public Dictionary<string, string> Export(IEnumerable<CompiledMesh> meshes, string textureDirectory)
    {
        var exported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (material, source) in CollectSources(meshes))
        {
            var dest = ImageCodec.ExportTexture(source.Path, textureDirectory, material, source.Tint);
            if (dest is not null)
            {
                exported[material] = dest;
            }
        }

        return exported;
    }

    private TextureSource? ResolveSource(MeshPart part, string? terrainRemapFolder)
    {
        var tint = part.Tint;
        foreach (var hint in CandidateNames(part))
        {
            var fromMaterial = TryWalkMaterial(hint, ref tint);
            if (fromMaterial is not null)
            {
                return WithRemap(fromMaterial, hint, tint, terrainRemapFolder);
            }

            var fromFile = FindImage(hint, terrainRemapFolder);
            if (fromFile is not null)
            {
                return new TextureSource(fromFile, tint);
            }
        }

        return null;
    }

    private TextureSource? TryWalkMaterial(string hint, ref Vec3? tint)
    {
        var materialPath = _store.Find(hint + ".Material.Gbx")
                           ?? _store.Find(hint + ".Shader.Gbx")
                           ?? _store.Find(hint);
        if (materialPath is null)
        {
            return null;
        }

        var node = _store.ParseNode(materialPath);
        if (node is CPlugMaterial material)
        {
            CollectTint(material, ref tint);
            var image = WalkMaterial(material);
            if (image is not null)
            {
                return new TextureSource(image, tint);
            }
        }
        else if (node is CPlugShader shader)
        {
            var image = WalkShader(shader);
            if (image is not null)
            {
                return new TextureSource(image, tint);
            }
        }
        else if (node is CPlugBitmap bitmap)
        {
            var image = ImagePath(bitmap);
            if (image is not null)
            {
                return new TextureSource(image, tint);
            }
        }

        return FindImageBeside(materialPath, hint) is { } beside
            ? new TextureSource(beside, tint)
            : null;
    }

    private string? WalkMaterial(CPlugMaterial material)
    {
        var custom = material.CustomMaterial;
        if (custom?.Textures is { Length: > 0 } textures)
        {
            var preferred = textures
                .Select(t => (t.Name, Path: NodeImage(t.Texture, t.TextureFile)))
                .Where(t => t.Path is not null)
                .OrderByDescending(t => ScoreName(t.Name))
                .FirstOrDefault();
            if (preferred.Path is not null)
            {
                return preferred.Path;
            }
        }

        foreach (var shader in EnumerateShaders(material))
        {
            var image = WalkShader(shader);
            if (image is not null)
            {
                return image;
            }
        }

        return WalkUnknownBitmaps(material);
    }

    private IEnumerable<CMwNod> EnumerateShaders(CPlugMaterial material)
    {
        if (ResolvePlug(material, "Shader") is { } root)
        {
            yield return root;
        }

        foreach (var device in material.DeviceMaterials ?? [])
        {
            foreach (var name in new[] { "Shader1", "Shader2", "Shader3" })
            {
                if (ResolvePlug(device, name) is { } shader)
                {
                    yield return shader;
                }
            }
        }
    }

    private CMwNod? ResolvePlug(object owner, string property)
    {
        var type = owner.GetType();
        var node = type.GetProperty(property)?.GetValue(owner) as CMwNod;
        var file = type.GetProperty(property + "File")?.GetValue(owner) as GbxRefTableFile;
        return _store.Resolve(file, node);
    }

    private string? WalkShader(CMwNod shader)
    {
        if (shader is CPlugShaderApply apply)
        {
            foreach (var address in apply.BitmapAddresses ?? [])
            {
                var image = SamplerImage(address);
                if (image is not null)
                {
                    return image;
                }
            }
        }

        if (shader is CPlugShader generic)
        {
            foreach (var pass in generic.Passes ?? [])
            {
                var image = WalkUnknownBitmaps(pass);
                if (image is not null)
                {
                    return image;
                }
            }
        }

        return WalkUnknownBitmaps(shader);
    }

    private string? SamplerImage(CPlugBitmapSampler sampler)
    {
        var bitmap = _store.Resolve(sampler.BitmapFile, sampler.Bitmap);
        return bitmap is null ? null : ImagePath(bitmap);
    }

    private string? NodeImage(CMwNod? node, GbxRefTableFile? file)
    {
        var resolved = node ?? (file is null ? null : _store.Resolve<CMwNod>(file));
        return resolved switch
        {
            CPlugBitmap bitmap => ImagePath(bitmap),
            CPlugBitmapSampler sampler => SamplerImage(sampler),
            _ => file is null ? null : FindImage(GbxPath.GetFileNameWithoutExtension(file.FilePath) ?? file.FilePath)
        };
    }

    private string? ImagePath(CPlugBitmap bitmap)
    {
        var file = bitmap.ImageFile?.FilePath;
        var found = _store.Find(file) ?? _store.Find(Path.GetFileName(file ?? ""));
        if (found is not null)
        {
            return found;
        }

        if (file is not null)
        {
            var stem = GbxPath.GetFileNameWithoutExtension(file) ?? Path.GetFileNameWithoutExtension(file);
            var byStem = FindImage(stem);
            if (byStem is not null)
            {
                return byStem;
            }
        }

        return null;
    }

    private string? WalkUnknownBitmaps(CMwNod node)
    {
        foreach (var prop in node.GetType().GetProperties())
        {
            object? value;
            try
            {
                value = prop.GetValue(node);
            }
            catch
            {
                continue;
            }

            switch (value)
            {
                case CPlugBitmap bitmap:
                    var direct = ImagePath(bitmap);
                    if (direct is not null)
                    {
                        return direct;
                    }

                    break;
                case CPlugBitmapSampler sampler:
                    var sampled = SamplerImage(sampler);
                    if (sampled is not null)
                    {
                        return sampled;
                    }

                    break;
                case GbxRefTableFile file when file.FilePath.Contains("Texture", StringComparison.OrdinalIgnoreCase)
                                               || file.FilePath.Contains(".dds", StringComparison.OrdinalIgnoreCase):
                    var fromRef = _store.Find(file.FilePath) ?? FindImage(GbxPath.GetFileNameWithoutExtension(file.FilePath) ?? file.FilePath);
                    if (fromRef is not null)
                    {
                        return fromRef;
                    }

                    break;
            }
        }

        return null;
    }

    private static void CollectTint(CPlugMaterial material, ref Vec3? tint)
    {
        if (tint is not null)
        {
            return;
        }

        var custom = material.CustomMaterial;
        foreach (var fx in (custom?.GpuFxs1 ?? []).Concat(custom?.GpuFxs2 ?? []))
        {
            var candidate = TintFromFx(fx);
            if (candidate is not null)
            {
                tint = candidate;
                return;
            }
        }

        foreach (var table in ReadColorTargets(material))
        {
            var candidate = TintFromNode(table);
            if (candidate is not null)
            {
                tint = candidate;
                return;
            }
        }
    }

    private static IEnumerable<CMwNod> ReadColorTargets(CPlugMaterial material)
    {
        foreach (var prop in material.GetType().GetProperties())
        {
            object? value;
            try
            {
                value = prop.GetValue(material);
            }
            catch
            {
                continue;
            }

            switch (value)
            {
                case CMwNod node when prop.Name.Contains("ColorTarget", StringComparison.OrdinalIgnoreCase):
                    yield return node;
                    break;
                case IEnumerable<CMwNod> nodes when prop.Name.Contains("ColorTarget", StringComparison.OrdinalIgnoreCase):
                    foreach (var node in nodes)
                    {
                        yield return node;
                    }

                    break;
            }
        }
    }

    private static Vec3? TintFromNode(CMwNod node)
    {
        foreach (var prop in node.GetType().GetProperties())
        {
            object? value;
            try
            {
                value = prop.GetValue(node);
            }
            catch
            {
                continue;
            }

            if (value is Vec3 color && IsUsefulTint(color))
            {
                return color;
            }

            if (value is float[] row && row.Length >= 3)
            {
                var candidate = new Vec3(row[0], row[1], row[2]);
                if (IsUsefulTint(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static Vec3? TintFromFx(CPlugMaterialCustom.GpuFx fx)
    {
        var name = fx.U01 ?? "";
        var looksColor = name.Contains("Color", StringComparison.OrdinalIgnoreCase)
                         || name.Contains("Diffuse", StringComparison.OrdinalIgnoreCase)
                         || name.Contains("Hue", StringComparison.OrdinalIgnoreCase)
                         || name.Contains("Tint", StringComparison.OrdinalIgnoreCase);
        foreach (var row in fx.U03 ?? [])
        {
            if (row is not { Length: >= 3 })
            {
                continue;
            }

            var color = new Vec3(row[0], row[1], row[2]);
            if (IsUsefulTint(color) && (looksColor || row.Length is 3 or 4))
            {
                return color;
            }
        }

        return null;
    }

    private static bool IsUsefulTint(Vec3 color)
    {
        if (color.X is < 0 or > 4 || color.Y is < 0 or > 4 || color.Z is < 0 or > 4)
        {
            return false;
        }

        var max = Math.Max(color.X, Math.Max(color.Y, color.Z));
        var min = Math.Min(color.X, Math.Min(color.Y, color.Z));
        return max > 0.02f && (max - min > 0.04f || max < 0.97f);
    }

    private TextureSource WithRemap(TextureSource source, string hint, Vec3? tint, string? remapFolder)
    {
        var remapped = FindImage(hint, remapFolder);
        return remapped is null ? source with { Tint = tint } : new TextureSource(remapped, tint);
    }

    private string? FindImage(string? hint, string? remapFolder = null)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return null;
        }

        var stems = new[] { hint, hint + "D", hint + "1", hint + "2", hint + "Color" };
        foreach (var stem in stems.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (remapFolder is not null)
            {
                foreach (var ext in new[] { ".dds", ".tga", ".png", ".jpg" })
                {
                    var remapped = _store.Find(Path.Combine(remapFolder, stem + ext))
                                   ?? _store.Find(stem + ext);
                    if (remapped is not null && remapped.Contains(remapFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        return remapped;
                    }
                }
            }

            foreach (var ext in new[] { ".dds", ".tga", ".png", ".jpg" })
            {
                var found = _store.Find(stem + ext);
                if (found is not null)
                {
                    return found;
                }
            }

            var textureGbx = _store.Find(stem + ".Texture.Gbx") ?? _store.Find(stem + ".dds.Gbx");
            if (textureGbx is not null)
            {
                var bitmap = _store.ParseNode<CPlugBitmap>(textureGbx);
                var image = bitmap is null ? null : ImagePath(bitmap);
                if (image is not null)
                {
                    return image;
                }

                var sibling = Path.ChangeExtension(textureGbx, ".dds");
                if (File.Exists(sibling))
                {
                    return sibling;
                }
            }
        }

        return null;
    }

    private string? FindImageBeside(string gbxPath, string hint)
    {
        var dir = Path.GetDirectoryName(gbxPath);
        if (dir is null)
        {
            return null;
        }

        foreach (var stem in new[] { hint, hint + "D", hint + "1" })
        {
            foreach (var ext in new[] { ".dds", ".tga", ".png" })
            {
                var beside = Path.Combine(dir, stem + ext);
                if (File.Exists(beside))
                {
                    return beside;
                }
            }
        }

        return Directory.EnumerateFiles(dir)
            .FirstOrDefault(f =>
            {
                var ext = Path.GetExtension(f);
                return (ext.Equals(".dds", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".tga", StringComparison.OrdinalIgnoreCase))
                       && Path.GetFileName(f).Contains(hint, StringComparison.OrdinalIgnoreCase);
            });
    }

    private static IEnumerable<string> CandidateNames(MeshPart part)
    {
        yield return part.MaterialName;
        if (!string.IsNullOrWhiteSpace(part.TextureHint))
        {
            yield return GbxPath.GetFileNameWithoutExtension(part.TextureHint) ?? part.TextureHint;
            yield return Path.GetFileNameWithoutExtension(part.TextureHint);
        }
    }

    private static int ScoreName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return 0;
        }

        if (name.Contains("Diffuse", StringComparison.OrdinalIgnoreCase)
            || name.Equals("D", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Color", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("D", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (name.Contains("Normal", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Spec", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("N", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("S", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        return 1;
    }
}

public sealed record TextureSource(string Path, Vec3? Tint);