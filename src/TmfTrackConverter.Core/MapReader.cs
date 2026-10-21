using GBX.NET;
using GBX.NET.Engines.Game;

namespace TmfTrackConverter.Core;

public sealed class MapReader
{
    public LoadedMap Load(string path, GameInstall install)
    {
        GbxBootstrap.EnsureInitialized();

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Track file not found.", path);
        }

        if (!path.EndsWith(".Challenge.Gbx", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".Map.Gbx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Please select a *.Challenge.Gbx track file.");
        }

        var challenge = Gbx.ParseNode<CGameCtnChallenge>(path);
        var collectionName = challenge.Collection?.ToString() ?? challenge.MapInfo.Collection.ToString();
        if (!TmfEnvironments.TryParse(collectionName, out var environment))
        {
            throw new InvalidDataException($"Unsupported environment '{collectionName}'.");
        }

        if (!install.Supports(environment))
        {
            throw new InvalidOperationException(
                $"This {install.Edition} install cannot open {environment.DisplayName()} tracks. " +
                $"Available environments: {string.Join(", ", install.AvailableEnvironments.Select(e => e.DisplayName()))}.");
        }

        var hashed = challenge.HashedPassword;
        var hasPassword = hashed is { } checksum && checksum != Checksum128.Zero;

        return new LoadedMap
        {
            FilePath = path,
            Challenge = challenge,
            MapName = string.IsNullOrWhiteSpace(challenge.MapName) ? Path.GetFileNameWithoutExtension(path) : challenge.MapName,
            Author = challenge.AuthorNickname ?? challenge.AuthorLogin,
            Environment = environment,
            CollectionName = collectionName,
            DecorationId = challenge.Decoration.Id,
            Size = challenge.Size,
            BlockSize = environment.GetBlockSize(),
            BlockCount = challenge.GetBlocks().Count(),
            HasPassword = hasPassword,
            Password = challenge.Password
        };
    }
}
