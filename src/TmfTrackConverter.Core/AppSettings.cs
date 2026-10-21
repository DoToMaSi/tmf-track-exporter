namespace TmfTrackConverter.Core;

public sealed class AppSettings
{
    public string? GameFolder { get; set; }

    public bool CacheReady { get; set; }

    public string? DefaultTracksFolder { get; set; }

    public bool IncludeEnvironmentMesh { get; set; } = true;

    public bool SkipClipBlocks { get; set; } = true;

    public bool ExportObjSidecar { get; set; }
}
