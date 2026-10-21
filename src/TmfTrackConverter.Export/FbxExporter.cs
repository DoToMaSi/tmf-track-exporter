using System.Globalization;
using System.Text;
using TmfTrackConverter.Core;

namespace TmfTrackConverter.Export;

public static class FbxExporter
{
    public static void Write(
        TrackScene scene,
        string path,
        Action<int, string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var fbx = new Writer(writer);
        fbx.WriteScene(scene, progress, cancellationToken);
    }

    private sealed class Writer
    {
        private readonly TextWriter _w;
        private readonly CultureInfo _inv = CultureInfo.InvariantCulture;
        private long _nextId = 1000;

        public Writer(TextWriter writer) => _w = writer;

        public void WriteScene(
            TrackScene scene,
            Action<int, string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var now = DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff", _inv);
            _w.WriteLine("; FBX 7.4.0 project file");
            _w.WriteLine("; Created by TMF Track Converter");
            _w.WriteLine();
            _w.WriteLine("FBXHeaderExtension:  {");
            _w.WriteLine("\tFBXHeaderVersion: 1003");
            _w.WriteLine("\tFBXVersion: 7400");
            _w.WriteLine("\tCreator: \"TMF Track Converter\"");
            _w.WriteLine($"\tCreationTimeStamp:  {{ Version: 1000 Year: {DateTime.Now.Year} Month: {DateTime.Now.Month} Day: {DateTime.Now.Day} Hour: {DateTime.Now.Hour} Minute: {DateTime.Now.Minute} Second: {DateTime.Now.Second} Millisecond: {DateTime.Now.Millisecond} }}");
            _w.WriteLine("}");
            _w.WriteLine();
            _w.WriteLine($"CreationTime: \"{now}\"");
            _w.WriteLine("Creator: \"TMF Track Converter\"");
            _w.WriteLine();
            _w.WriteLine("GlobalSettings:  {");
            _w.WriteLine("\tVersion: 1000");
            _w.WriteLine("\tProperties70:  {");
            _w.WriteLine("\t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",1");
            _w.WriteLine("\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",1");
            _w.WriteLine("\t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",2");
            _w.WriteLine("\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1");
            _w.WriteLine("\t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0");
            _w.WriteLine("\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1");
            _w.WriteLine("\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",1");
            _w.WriteLine("\t}");
            _w.WriteLine("}");
            _w.WriteLine();

            var rootId = NextId();
            var geometryIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var geometryYaw = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var materialIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var textureIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var videoIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var modelIds = new List<(long Id, string Name, string MeshKey, string GeometryKey)>();

            foreach (var mesh in scene.Meshes.Values)
            {
                foreach (var part in mesh.Parts)
                {
                    if (materialIds.ContainsKey(part.MaterialName))
                    {
                        continue;
                    }

                    materialIds[part.MaterialName] = NextId();
                    if (scene.MaterialTextures.ContainsKey(part.MaterialName))
                    {
                        textureIds[part.MaterialName] = NextId();
                        videoIds[part.MaterialName] = NextId();
                    }
                }
            }

            foreach (var instance in scene.Instances)
            {
                var geometryKey = GeometryKey(instance.MeshKey, instance.YawDegrees);
                if (!geometryIds.ContainsKey(geometryKey))
                {
                    geometryIds[geometryKey] = NextId();
                    geometryYaw[geometryKey] = instance.YawDegrees;
                }

                modelIds.Add((NextId(), instance.Name, instance.MeshKey, geometryKey));
            }

            _w.WriteLine("Documents:  {");
            _w.WriteLine("\tCount: 1");
            _w.WriteLine($"\tDocument: {NextId()}, \"\", \"Scene\" {{");
            _w.WriteLine("\t\tProperties70:  {");
            _w.WriteLine($"\t\t\tP: \"SourceObject\", \"object\", \"\", \"\"");
            _w.WriteLine("\t\t\tP: \"ActiveAnimStackName\", \"KString\", \"\", \"\", \"\"");
            _w.WriteLine("\t\t}");
            _w.WriteLine($"\t\tRootNode: {rootId}");
            _w.WriteLine("\t}");
            _w.WriteLine("}");
            _w.WriteLine();
            _w.WriteLine("Definitions:  {");
            _w.WriteLine("\tVersion: 100");
            _w.WriteLine($"\tCount: {4 + geometryIds.Count}");
            _w.WriteLine("\tObjectType: \"Model\" { Count: " + modelIds.Count + " }");
            _w.WriteLine("\tObjectType: \"Geometry\" { Count: " + geometryIds.Count + " }");
            _w.WriteLine("\tObjectType: \"Material\" { Count: " + materialIds.Count + " }");
            _w.WriteLine("\tObjectType: \"Texture\" { Count: " + textureIds.Count + " }");
            _w.WriteLine("}");
            _w.WriteLine();
            _w.WriteLine("Objects:  {");

            var meshIndex = 0;
            foreach (var (geometryKey, geometryId) in geometryIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var meshKey = MeshKeyFromGeometryKey(geometryKey);
                if (!scene.Meshes.TryGetValue(meshKey, out var mesh))
                {
                    continue;
                }

                progress?.Invoke(meshIndex, geometryKey);
                WriteGeometry(geometryId, geometryKey, mesh, geometryYaw[geometryKey]);
                meshIndex++;
            }

            for (var i = 0; i < scene.Instances.Count; i++)
            {
                WriteModel(modelIds[i].Id, scene.Instances[i]);
            }

            foreach (var (name, id) in materialIds)
            {
                WriteMaterial(id, name);
            }

            foreach (var (name, id) in textureIds)
            {
                var file = scene.MaterialTextures[name];
                WriteTexture(id, name, file);
                WriteVideo(videoIds[name], name, file);
            }

            _w.WriteLine("}");
            _w.WriteLine();
            _w.WriteLine("Connections:  {");
            foreach (var (id, _, meshKey, geometryKey) in modelIds)
            {
                _w.WriteLine($"\tC: \"OO\",{id},{rootId}");
                if (geometryIds.TryGetValue(geometryKey, out var geo))
                {
                    _w.WriteLine($"\tC: \"OO\",{geo},{id}");
                }

                if (scene.Meshes.TryGetValue(meshKey, out var mesh))
                {
                    foreach (var part in mesh.Parts)
                    {
                        if (materialIds.TryGetValue(part.MaterialName, out var mat))
                        {
                            _w.WriteLine($"\tC: \"OO\",{mat},{id}");
                        }
                    }
                }
            }

            foreach (var (name, texId) in textureIds)
            {
                if (materialIds.TryGetValue(name, out var mat))
                {
                    _w.WriteLine($"\tC: \"OP\",{texId},{mat}, \"DiffuseColor\"");
                }

                if (videoIds.TryGetValue(name, out var video))
                {
                    _w.WriteLine($"\tC: \"OO\",{video},{texId}");
                }
            }

            _w.WriteLine("}");
        }

        private static string GeometryKey(string meshKey, float yawDegrees)
        {
            var yaw = (int)MathF.Round(yawDegrees);
            return yaw == 0 ? meshKey : $"{meshKey}@{yaw}";
        }

        private static string MeshKeyFromGeometryKey(string geometryKey)
        {
            var at = geometryKey.LastIndexOf('@');
            return at < 0 ? geometryKey : geometryKey[..at];
        }

        private void WriteGeometry(long id, string geometryKey, CompiledMesh mesh, float yawDegrees)
        {
            var vertices = new List<string>();
            var normals = new List<string>();
            var uvs = new List<string>();
            var indices = new List<string>();
            var materials = new List<int>();
            var materialNames = mesh.Parts.Select(p => p.MaterialName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var radians = yawDegrees * MathF.PI / 180f;
            var cos = MathF.Cos(radians);
            var sin = MathF.Sin(radians);
            var rotate = MathF.Abs(yawDegrees) > 0.01f;

            var offset = 0;
            foreach (var part in mesh.Parts)
            {
                var matIndex = materialNames.FindIndex(n => string.Equals(n, part.MaterialName, StringComparison.OrdinalIgnoreCase));
                foreach (var v in part.Vertices)
                {
                    var p = rotate ? BlockPlacement.RotateY(v.Position, cos, sin) : v.Position;
                    var n = rotate ? BlockPlacement.RotateY(v.Normal, cos, sin) : v.Normal;
                    vertices.Add($"{F(p.X)},{F(p.Y)},{F(p.Z)}");
                    normals.Add($"{F(n.X)},{F(n.Y)},{F(n.Z)}");
                    uvs.Add($"{F(v.Uv.X)},{F(v.Uv.Y)}");
                }

                for (var i = 0; i + 2 < part.Indices.Count; i += 3)
                {
                    var a = offset + part.Indices[i];
                    var b = offset + part.Indices[i + 1];
                    var c = offset + part.Indices[i + 2];
                    indices.Add($"{a},{b},{-c - 1}");
                    materials.Add(matIndex);
                }

                offset += part.Vertices.Count;
            }

            _w.WriteLine($"\tGeometry: {id}, \"Geometry::{Sanitize(geometryKey)}\", \"Mesh\" {{");
            _w.WriteLine($"\t\tVertices: *{vertices.Count * 3} {{");
            _w.WriteLine($"\t\t\ta: {string.Join(",", vertices)}");
            _w.WriteLine("\t\t}");
            _w.WriteLine($"\t\tPolygonVertexIndex: *{indices.Count * 3} {{");
            _w.WriteLine($"\t\t\ta: {string.Join(",", indices)}");
            _w.WriteLine("\t\t}");
            _w.WriteLine("\t\tGeometryVersion: 124");
            _w.WriteLine("\t\tLayerElementNormal: 0 {");
            _w.WriteLine("\t\t\tVersion: 101");
            _w.WriteLine("\t\t\tName: \"\"");
            _w.WriteLine("\t\t\tMappingInformationType: \"ByVertice\"");
            _w.WriteLine("\t\t\tReferenceInformationType: \"Direct\"");
            _w.WriteLine($"\t\t\tNormals: *{normals.Count * 3} {{");
            _w.WriteLine($"\t\t\t\ta: {string.Join(",", normals)}");
            _w.WriteLine("\t\t\t}");
            _w.WriteLine("\t\t}");
            _w.WriteLine("\t\tLayerElementUV: 0 {");
            _w.WriteLine("\t\t\tVersion: 101");
            _w.WriteLine("\t\t\tName: \"UVMap\"");
            _w.WriteLine("\t\t\tMappingInformationType: \"ByVertice\"");
            _w.WriteLine("\t\t\tReferenceInformationType: \"Direct\"");
            _w.WriteLine($"\t\t\tUV: *{uvs.Count * 2} {{");
            _w.WriteLine($"\t\t\t\ta: {string.Join(",", uvs)}");
            _w.WriteLine("\t\t\t}");
            _w.WriteLine("\t\t}");
            _w.WriteLine("\t\tLayerElementMaterial: 0 {");
            _w.WriteLine("\t\t\tVersion: 101");
            _w.WriteLine("\t\t\tName: \"\"");
            _w.WriteLine("\t\t\tMappingInformationType: \"ByPolygon\"");
            _w.WriteLine("\t\t\tReferenceInformationType: \"IndexToDirect\"");
            _w.WriteLine($"\t\t\tMaterials: *{materials.Count} {{");
            _w.WriteLine($"\t\t\t\ta: {string.Join(",", materials)}");
            _w.WriteLine("\t\t\t}");
            _w.WriteLine("\t\t}");
            _w.WriteLine("\t\tLayer: 0 {");
            _w.WriteLine("\t\t\tVersion: 100");
            _w.WriteLine("\t\t\tLayerElement:  { Type: \"LayerElementNormal\" TypedIndex: 0 }");
            _w.WriteLine("\t\t\tLayerElement:  { Type: \"LayerElementUV\" TypedIndex: 0 }");
            _w.WriteLine("\t\t\tLayerElement:  { Type: \"LayerElementMaterial\" TypedIndex: 0 }");
            _w.WriteLine("\t\t}");
            _w.WriteLine("\t}");
        }

        private void WriteModel(long id, TrackInstance instance)
        {
            _w.WriteLine($"\tModel: {id}, \"Model::{Sanitize(instance.Name)}\", \"Mesh\" {{");
            _w.WriteLine("\t\tVersion: 232");
            _w.WriteLine("\t\tProperties70:  {");
            _w.WriteLine("\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
            _w.WriteLine("\t\t\tP: \"DefaultAttributeIndex\", \"int\", \"Integer\", \"\",0");
            _w.WriteLine($"\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",{F(instance.Position.X)},{F(instance.Position.Y)},{F(instance.Position.Z)}");
            _w.WriteLine("\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\",0,0,0");
            _w.WriteLine("\t\t}");
            _w.WriteLine("\t\tShading: T");
            _w.WriteLine("\t\tCulling: \"CullingOff\"");
            _w.WriteLine("\t}");
        }

        private void WriteMaterial(long id, string name)
        {
            _w.WriteLine($"\tMaterial: {id}, \"Material::{Sanitize(name)}\", \"\" {{");
            _w.WriteLine("\t\tVersion: 102");
            _w.WriteLine("\t\tShadingModel: \"phong\"");
            _w.WriteLine("\t\tMultiLayer: 0");
            _w.WriteLine("\t\tProperties70:  {");
            _w.WriteLine("\t\t\tP: \"DiffuseColor\", \"Color\", \"\", \"A\",1,1,1");
            _w.WriteLine("\t\t\tP: \"SpecularColor\", \"Color\", \"\", \"A\",0.2,0.2,0.2");
            _w.WriteLine("\t\t}");
            _w.WriteLine("\t}");
        }

        private void WriteTexture(long id, string name, string file)
        {
            var relative = Path.Combine("textures", Path.GetFileName(file)).Replace('\\', '/');
            _w.WriteLine($"\tTexture: {id}, \"Texture::{Sanitize(name)}\", \"\" {{");
            _w.WriteLine("\t\tType: \"TextureVideoClip\"");
            _w.WriteLine("\t\tVersion: 202");
            _w.WriteLine($"\t\tTextureName: \"Texture::{Sanitize(name)}\"");
            _w.WriteLine("\t\tProperties70:  {");
            _w.WriteLine("\t\t\tP: \"CurrentTextureBlendMode\", \"enum\", \"\", \"\",0");
            _w.WriteLine("\t\t\tP: \"UVSet\", \"KString\", \"\", \"\", \"UVMap\"");
            _w.WriteLine("\t\t}");
            _w.WriteLine("\t\tMedia: \"Video::" + Sanitize(name) + "\"");
            _w.WriteLine($"\t\tFileName: \"{Escape(file)}\"");
            _w.WriteLine($"\t\tRelativeFilename: \"{relative}\"");
            _w.WriteLine("\t}");
        }

        private void WriteVideo(long id, string name, string file)
        {
            var relative = Path.Combine("textures", Path.GetFileName(file)).Replace('\\', '/');
            _w.WriteLine($"\tVideo: {id}, \"Video::{Sanitize(name)}\", \"Clip\" {{");
            _w.WriteLine("\t\tType: \"Clip\"");
            _w.WriteLine("\t\tProperties70:  {");
            _w.WriteLine($"\t\t\tP: \"Path\", \"KString\", \"XRefUrl\", \"\", \"{Escape(file)}\"");
            _w.WriteLine("\t\t}");
            _w.WriteLine("\t\tUseMipMap: 0");
            _w.WriteLine($"\t\tFilename: \"{Escape(file)}\"");
            _w.WriteLine($"\t\tRelativeFilename: \"{relative}\"");
            _w.WriteLine("\t}");
        }

        private long NextId() => _nextId++;

        private string F(float value) => value.ToString("0.######", _inv);

        private static string Sanitize(string name)
        {
            return string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
        }

        private static string Escape(string path) => path.Replace('\\', '/').Replace("\"", "\\\"");
    }
}
