using GBX.NET;

namespace TmfTrackConverter.Core;

public sealed class MeshVertex
{
    public Vec3 Position { get; set; }

    public Vec3 Normal { get; set; }

    public Vec2 Uv { get; set; }
}

public sealed class MeshPart
{
    public string MaterialName { get; set; } = "Default";

    public string? TextureHint { get; set; }

    public Vec3? Tint { get; set; }

    public List<MeshVertex> Vertices { get; } = [];

    public List<int> Indices { get; } = [];
}

public sealed class CompiledMesh
{
    public required string Key { get; init; }

    public List<MeshPart> Parts { get; } = [];

    public Vec3 LocalOrigin { get; set; }

    public Int3 FootprintCells { get; set; } = new(1, 1, 1);

    public List<Int3> UnitOffsets { get; set; } = [];

    public bool IsFlat { get; set; }

    public bool IsEmpty => Parts.Count == 0 || Parts.All(p => p.Indices.Count == 0);
}

public sealed class TrackInstance
{
    public required string Name { get; init; }

    public required string MeshKey { get; init; }

    public required Vec3 Position { get; init; }

    public float YawDegrees { get; init; }

    public string BlockName { get; init; } = "";

    public Int3 Coord { get; init; }

    public Direction Direction { get; init; }

    public bool IsGround { get; init; }

    public IReadOnlyList<Int3> OccupiedCells { get; init; } = [];
}

public sealed class TrackScene
{
    public required LoadedMap Map { get; init; }

    public Dictionary<string, CompiledMesh> Meshes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<TrackInstance> Instances { get; } = [];

    public List<string> Warnings { get; } = [];

    public Dictionary<string, string> MaterialTextures { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> MissingTextures { get; } = [];

    public string? TerrainRemapFolder { get; set; }
}
