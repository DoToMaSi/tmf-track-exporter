namespace TmfTrackConverter.Export;

public sealed class ExportOptions
{
    public required string FbxPath { get; init; }

    public bool WriteObjSidecar { get; init; }

    public bool IncludeEnvironment { get; init; } = true;

    public string TextureDirectory => Path.Combine(Path.GetDirectoryName(FbxPath) ?? ".", "textures");
}
