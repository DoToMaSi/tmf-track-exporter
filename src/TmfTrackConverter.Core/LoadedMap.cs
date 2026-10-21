using GBX.NET;
using GBX.NET.Engines.Game;

namespace TmfTrackConverter.Core;

public sealed class LoadedMap
{
    public required string FilePath { get; init; }

    public required CGameCtnChallenge Challenge { get; init; }

    public required string MapName { get; init; }

    public string? Author { get; init; }

    public required TmfEnvironment Environment { get; init; }

    public required string CollectionName { get; init; }

    public required string DecorationId { get; init; }

    public required Int3 Size { get; init; }

    public required Int3 BlockSize { get; init; }

    public required int BlockCount { get; init; }

    public bool HasPassword { get; init; }

    public string? Password { get; init; }
}
