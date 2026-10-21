using GBX.NET;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.Engines.Plug;

namespace TmfTrackConverter.Core;

public static class MeshBaker
{
    public static CompiledMesh? BakeAny(GameAssetStore store, CMwNod? node, string key)
    {
        var solids = BakeEach(store, node, key);
        return solids.Count switch
        {
            0 => null,
            1 => solids[0],
            _ => Combine(key, solids)
        };
    }

    public static List<CompiledMesh> BakeEach(GameAssetStore store, CMwNod? node, string key)
    {
        var solids = new List<CompiledMesh>();
        CollectSolids(store, node, key, solids, []);
        return solids;
    }

    private static void CollectSolids(
        GameAssetStore store,
        object? node,
        string key,
        List<CompiledMesh> solids,
        HashSet<object> seen)
    {
        if (node is null || !seen.Add(node))
        {
            return;
        }

        if (node is CPlugSolid solid)
        {
            var baked = BakeSolid(solid, key + "#" + solids.Count, store);
            if (!baked.IsEmpty)
            {
                solids.Add(baked);
            }

            return;
        }

        if (node is System.Collections.IEnumerable items and not string)
        {
            foreach (var item in items)
            {
                CollectSolids(store, item, key, solids, seen);
            }
        }

        foreach (var prop in node.GetType().GetProperties())
        {
            if (prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? value;
            try
            {
                value = prop.GetValue(node);
            }
            catch
            {
                continue;
            }

            if (value is CPlugSolid or CMwNod or System.Collections.IEnumerable)
            {
                CollectSolids(store, value, key, solids, seen);
            }
        }
    }

    public static CompiledMesh BakeSolid(CPlugSolid solid, string key, GameAssetStore? store = null)
    {
        return BakeSolid(solid, key, store, []);
    }

    private static CompiledMesh BakeSolid(
        CPlugSolid solid,
        string key,
        GameAssetStore? store,
        HashSet<CPlugSolid> seen)
    {
        if (!seen.Add(solid))
        {
            return new CompiledMesh { Key = key };
        }

        var tree = solid.Tree ?? store?.Resolve<CPlug>(solid.TreeFile);
        if (tree is CPlugSolid nested)
        {
            return BakeSolid(nested, key, store, seen);
        }

        var mesh = new CompiledMesh { Key = key };
        var parts = new Dictionary<string, MeshPart>(StringComparer.OrdinalIgnoreCase);
        var root = tree as CPlugTree;
        if (root is null)
        {
            return mesh;
        }

        foreach (var (node, loc) in WalkTree(root, root.Location ?? Iso4.Identity))
        {
            if (node.Visual is not CPlugVisualIndexedTriangles visual || visual.IndexBuffer is null)
            {
                continue;
            }

            var materialName = ResolveMaterialName(node);
            if (!parts.TryGetValue(materialName, out var part))
            {
                part = new MeshPart
                {
                    MaterialName = materialName,
                    TextureHint = node.ShaderFile?.FilePath ?? node.MaterialFile?.FilePath
                };
                parts[materialName] = part;
                mesh.Parts.Add(part);
            }

            var positions = GetPositions(visual);
            var normals = GetNormals(visual);
            var uvs = GetUvs(visual);

            foreach (var index in visual.IndexBuffer.Indices)
            {
                if (index < 0 || index >= positions.Length)
                {
                    continue;
                }

                var pos = TransformPoint(positions[index], loc);
                var nrm = index < normals.Length
                    ? TransformNormal(normals[index], loc).GetNormalized()
                    : new Vec3(0, 1, 0);
                var uv = index < uvs.Length ? uvs[index] : Vec2.Zero;

                part.Indices.Add(part.Vertices.Count);
                part.Vertices.Add(new MeshVertex
                {
                    Position = pos,
                    Normal = nrm,
                    Uv = uv
                });
            }
        }

        return mesh;
    }

    public static CompiledMesh Combine(string key, IEnumerable<CompiledMesh> meshes, bool recenter = true)
    {
        var combined = new CompiledMesh { Key = key };
        var parts = new Dictionary<string, MeshPart>(StringComparer.OrdinalIgnoreCase);

        foreach (var mesh in meshes)
        {
            foreach (var source in mesh.Parts)
            {
                if (!parts.TryGetValue(source.MaterialName, out var part))
                {
                    part = new MeshPart
                    {
                        MaterialName = source.MaterialName,
                        TextureHint = source.TextureHint,
                        Tint = source.Tint
                    };
                    parts[source.MaterialName] = part;
                    combined.Parts.Add(part);
                }

                var offset = part.Vertices.Count;
                part.Vertices.AddRange(source.Vertices);
                part.Indices.AddRange(source.Indices.Select(i => i + offset));
            }
        }

        if (recenter)
        {
            Recenter(combined);
        }

        return combined;
    }

    public static bool HasTinyUvs(CompiledMesh mesh, float minSpan = 0.2f)
    {
        if (mesh.IsEmpty)
        {
            return false;
        }

        var minU = float.MaxValue;
        var minV = float.MaxValue;
        var maxU = float.MinValue;
        var maxV = float.MinValue;
        var any = false;
        foreach (var part in mesh.Parts)
        {
            foreach (var vertex in part.Vertices)
            {
                any = true;
                minU = Math.Min(minU, vertex.Uv.X);
                minV = Math.Min(minV, vertex.Uv.Y);
                maxU = Math.Max(maxU, vertex.Uv.X);
                maxV = Math.Max(maxV, vertex.Uv.Y);
            }
        }

        return !any || (maxU - minU < minSpan && maxV - minV < minSpan);
    }

    public static void ApplyPlanarXzUvs(CompiledMesh mesh, float tileX, float tileZ)
    {
        tileX = Math.Max(1f, tileX);
        tileZ = Math.Max(1f, tileZ);
        foreach (var part in mesh.Parts)
        {
            for (var i = 0; i < part.Vertices.Count; i++)
            {
                var vertex = part.Vertices[i];
                var world = vertex.Position + mesh.LocalOrigin;
                vertex.Uv = new Vec2(world.X / tileX, world.Z / tileZ);
                part.Vertices[i] = vertex;
            }
        }
    }

    public static void Recenter(CompiledMesh mesh)
    {
        if (mesh.IsEmpty)
        {
            mesh.LocalOrigin = Vec3.Zero;
            return;
        }

        var min = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vec3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var part in mesh.Parts)
        {
            foreach (var vertex in part.Vertices)
            {
                min = new Vec3(
                    Math.Min(min.X, vertex.Position.X),
                    Math.Min(min.Y, vertex.Position.Y),
                    Math.Min(min.Z, vertex.Position.Z));
                max = new Vec3(
                    Math.Max(max.X, vertex.Position.X),
                    Math.Max(max.Y, vertex.Position.Y),
                    Math.Max(max.Z, vertex.Position.Z));
            }
        }

        var center = new Vec3((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, (min.Z + max.Z) * 0.5f);
        mesh.LocalOrigin = center;
        foreach (var part in mesh.Parts)
        {
            for (var i = 0; i < part.Vertices.Count; i++)
            {
                var vertex = part.Vertices[i];
                vertex.Position = new Vec3(
                    vertex.Position.X - center.X,
                    vertex.Position.Y - center.Y,
                    vertex.Position.Z - center.Z);
                part.Vertices[i] = vertex;
            }
        }
    }

    private static IEnumerable<(CPlugTree Tree, Iso4 Location)> WalkTree(CPlugTree tree, Iso4 location)
    {
        if (tree is CPlugTreeVisualMip mip)
        {
            var lod = mip.Levels.OrderBy(level => level.FarZ).Select(level => level.Tree).FirstOrDefault();
            if (lod is null)
            {
                yield break;
            }

            var lodLocation = Compose(location, lod.Location ?? Iso4.Identity);
            foreach (var pair in WalkTree(lod, lodLocation))
            {
                yield return pair;
            }

            yield break;
        }

        yield return (tree, location);

        foreach (var child in tree.Children ?? [])
        {
            var childLocation = Compose(location, child.Location ?? Iso4.Identity);
            foreach (var pair in WalkTree(child, childLocation))
            {
                yield return pair;
            }
        }
    }

    private static Iso4 Compose(Iso4 a, Iso4 b)
    {
        return new Iso4(
            a.XX * b.XX + a.XY * b.YX + a.XZ * b.ZX,
            a.XX * b.XY + a.XY * b.YY + a.XZ * b.ZY,
            a.XX * b.XZ + a.XY * b.YZ + a.XZ * b.ZZ,
            a.YX * b.XX + a.YY * b.YX + a.YZ * b.ZX,
            a.YX * b.XY + a.YY * b.YY + a.YZ * b.ZY,
            a.YX * b.XZ + a.YY * b.YZ + a.YZ * b.ZZ,
            a.ZX * b.XX + a.ZY * b.YX + a.ZZ * b.ZX,
            a.ZX * b.XY + a.ZY * b.YY + a.ZZ * b.ZY,
            a.ZX * b.XZ + a.ZY * b.YZ + a.ZZ * b.ZZ,
            a.XX * b.TX + a.XY * b.TY + a.XZ * b.TZ + a.TX,
            a.YX * b.TX + a.YY * b.TY + a.YZ * b.TZ + a.TY,
            a.ZX * b.TX + a.ZY * b.TY + a.ZZ * b.TZ + a.TZ);
    }

    private static string ResolveMaterialName(CPlugTree tree)
    {
        if (tree.ShaderFile?.FilePath is { Length: > 0 } shaderPath)
        {
            return Sanitize(GbxPath.GetFileNameWithoutExtension(shaderPath) ?? "Material");
        }

        if (tree.MaterialFile?.FilePath is { Length: > 0 } materialPath)
        {
            return Sanitize(GbxPath.GetFileNameWithoutExtension(materialPath) ?? "Material");
        }

        if (tree.Material is CPlugMaterial && tree.Material.GetType().Name != nameof(CPlugMaterial))
        {
            return Sanitize(tree.Material.GetType().Name);
        }

        return "Unknown";
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    private static Vec3[] GetPositions(CPlugVisualIndexedTriangles visual)
    {
        if (visual.VertexStreams.Count > 0 && visual.VertexStreams[0].Positions is { Length: > 0 } stream)
        {
            return stream;
        }

        return visual.Vertices.Select(v => v.Position).ToArray();
    }

    private static Vec3[] GetNormals(CPlugVisualIndexedTriangles visual)
    {
        if (visual.VertexStreams.Count > 0 && visual.VertexStreams[0].Normals is { Length: > 0 } stream)
        {
            return stream;
        }

        return visual.Vertices.Select(v => v.Normal ?? new Vec3(0, 1, 0)).ToArray();
    }

    private static Vec2[] GetUvs(CPlugVisualIndexedTriangles visual)
    {
        Vec2[]? fallback = null;

        foreach (var set in visual.TexCoords)
        {
            var uvs = set.TexCoords.Select(t => t.UV).ToArray();
            if (uvs.Length == 0)
            {
                continue;
            }

            fallback ??= uvs;
            if (UvSpanLooksUsable(uvs))
            {
                return uvs;
            }
        }

        foreach (var stream in visual.VertexStreams)
        {
            foreach (var uvs in stream.UVs.Values)
            {
                if (uvs is not { Length: > 0 })
                {
                    continue;
                }

                fallback ??= uvs;
                if (UvSpanLooksUsable(uvs))
                {
                    return uvs;
                }
            }
        }

        return fallback ?? [];
    }

    private static bool UvSpanLooksUsable(Vec2[] uvs, float minSpan = 0.2f)
    {
        var minU = float.MaxValue;
        var minV = float.MaxValue;
        var maxU = float.MinValue;
        var maxV = float.MinValue;
        foreach (var uv in uvs)
        {
            minU = Math.Min(minU, uv.X);
            minV = Math.Min(minV, uv.Y);
            maxU = Math.Max(maxU, uv.X);
            maxV = Math.Max(maxV, uv.Y);
        }

        return maxU - minU >= minSpan || maxV - minV >= minSpan;
    }

    private static Vec3 TransformPoint(Vec3 pos, Iso4 loc)
    {
        return new Vec3(
            pos.X * loc.XX + pos.Y * loc.XY + pos.Z * loc.XZ + loc.TX,
            pos.X * loc.YX + pos.Y * loc.YY + pos.Z * loc.YZ + loc.TY,
            pos.X * loc.ZX + pos.Y * loc.ZY + pos.Z * loc.ZZ + loc.TZ);
    }

    private static Vec3 TransformNormal(Vec3 normal, Iso4 loc)
    {
        return new Vec3(
            normal.X * loc.XX + normal.Y * loc.XY + normal.Z * loc.XZ,
            normal.X * loc.YX + normal.Y * loc.YY + normal.Z * loc.YZ,
            normal.X * loc.ZX + normal.Y * loc.ZY + normal.Z * loc.ZZ);
    }
}
