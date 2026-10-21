using System.Globalization;
using System.Text;
using TmfTrackConverter.Core;

namespace TmfTrackConverter.Export;

public static class ObjTrackExporter
{
    public static void Write(
        TrackScene scene,
        string objPath,
        Action<int, string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var mtlPath = Path.ChangeExtension(objPath, ".mtl");
        var inv = CultureInfo.InvariantCulture;

        using var obj = new StreamWriter(objPath, false, Encoding.UTF8);
        using var mtl = new StreamWriter(mtlPath, false, Encoding.UTF8);

        obj.WriteLine("# TMF Track Converter");
        obj.WriteLine($"mtllib {Path.GetFileName(mtlPath)}");
        obj.WriteLine();

        var writtenMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, texture) in scene.MaterialTextures)
        {
            mtl.WriteLine($"newmtl {name}");
            mtl.WriteLine("Kd 1.0000 1.0000 1.0000");
            mtl.WriteLine("Ka 1.0000 1.0000 1.0000");
            mtl.WriteLine("Ks 0.2000 0.2000 0.2000");
            mtl.WriteLine("d 1.0000");
            mtl.WriteLine("illum 2");
            mtl.WriteLine($"map_Kd textures/{Path.GetFileName(texture)}");
            mtl.WriteLine();
            writtenMaterials.Add(name);
        }

        var vertexOffset = 1;
        var instanceIndex = 0;
        foreach (var instance in scene.Instances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (instanceIndex == 0 || instanceIndex % 10 == 0)
            {
                progress?.Invoke(instanceIndex, instance.Name);
            }

            instanceIndex++;
            if (!scene.Meshes.TryGetValue(instance.MeshKey, out var mesh))
            {
                continue;
            }

            obj.WriteLine($"o {Sanitize(instance.Name)}");
            var radians = instance.YawDegrees * MathF.PI / 180f;
            var cos = MathF.Cos(radians);
            var sin = MathF.Sin(radians);

            foreach (var part in mesh.Parts)
            {
                foreach (var vertex in part.Vertices)
                {
                    var p = BlockPlacement.RotateY(vertex.Position, cos, sin) + instance.Position;
                    var n = BlockPlacement.RotateY(vertex.Normal, cos, sin);
                    obj.WriteLine($"v {p.X.ToString(inv)} {p.Y.ToString(inv)} {p.Z.ToString(inv)}");
                    obj.WriteLine($"vn {n.X.ToString(inv)} {n.Y.ToString(inv)} {n.Z.ToString(inv)}");
                    obj.WriteLine($"vt {vertex.Uv.X.ToString(inv)} {vertex.Uv.Y.ToString(inv)}");
                }
            }

            var local = 0;
            foreach (var part in mesh.Parts)
            {
                if (!writtenMaterials.Contains(part.MaterialName))
                {
                    mtl.WriteLine($"newmtl {part.MaterialName}");
                    mtl.WriteLine("Kd 0.8000 0.8000 0.8000");
                    mtl.WriteLine();
                    writtenMaterials.Add(part.MaterialName);
                }

                obj.WriteLine($"g {Sanitize(instance.Name)}_{Sanitize(part.MaterialName)}");
                obj.WriteLine($"usemtl {part.MaterialName}");
                for (var i = 0; i + 2 < part.Indices.Count; i += 3)
                {
                    var a = vertexOffset + local + part.Indices[i];
                    var b = vertexOffset + local + part.Indices[i + 1];
                    var c = vertexOffset + local + part.Indices[i + 2];
                    obj.WriteLine($"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}");
                }

                local += part.Vertices.Count;
            }

            vertexOffset += mesh.Parts.Sum(p => p.Vertices.Count);
        }
    }

    private static string Sanitize(string name)
    {
        return string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
    }
}
