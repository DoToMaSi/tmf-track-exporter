using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.Scene;
using GBX.NET.Exceptions;
using GBX.NET.PAK;

namespace TmfTrackConverter.Core;

public sealed class PakDecryptor
{
    private static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bik", ".wav", ".ogg", ".html", ".mo", ".mp3", ".avi"
    };

    public async Task DecryptAsync(
        GameInstall install,
        IProgress<DecryptProgress>? progress,
        CancellationToken cancellationToken)
    {
        GbxBootstrap.EnsureInitialized();

        var staging = AppPaths.CacheStaging;
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, true);
        }

        Directory.CreateDirectory(staging);

        try
        {
            progress?.Report(new DecryptProgress("Reading packlist.dat…", 0, 1));
            var keys = (await PakList.ParseAsync(install.PackListPath, PakListGame.TM, cancellationToken))
                .ToKeyInfoDictionary();

            progress?.Report(new DecryptProgress("Resolving hashed file names…", 0, 1));
            var hashes = await Pak.BruteforceFileHashesAsync(
                install.PacksFolder,
                keys,
                progress: null,
                keepUnresolvedHashes: false,
                cancellationToken: cancellationToken);

            var pakPaths = Directory.GetFiles(install.PacksFolder, "*.pak")
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var extracted = 0;
            var estimated = Math.Max(1, pakPaths.Count * 800);

            foreach (var pakPath in pakPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pakId = Path.GetFileNameWithoutExtension(pakPath);
                progress?.Report(new DecryptProgress($"Opening {pakId}.pak…", extracted, estimated, pakId));

                await using var pak = keys.TryGetValue(pakId, out var key)
                    ? await Pak.ParseAsync(pakPath, key, cancellationToken: cancellationToken)
                    : await Pak.ParseAsync(pakPath, cancellationToken: cancellationToken);

                var extractRoot = Path.Combine(staging, pakId);
                Directory.CreateDirectory(extractRoot);

                foreach (var file in pak.Files.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var fileName = hashes.GetValueOrDefault(file.Name)?.Replace('\\', Path.DirectorySeparatorChar)
                                   ?? file.Name;
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        fileName = file.Name;
                    }

                    if (ShouldSkip(fileName))
                    {
                        continue;
                    }

                    var fullPath = Path.Combine(extractRoot, file.FolderPath, fileName);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

                    extracted++;
                    if (extracted % 25 == 0)
                    {
                        progress?.Report(new DecryptProgress(
                            $"Extracting {pakId}…",
                            extracted,
                            Math.Max(estimated, extracted + 1),
                            Path.Combine(file.FolderPath, fileName)));
                    }

                    try
                    {
                        ExtractFile(pak, file, fullPath);
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            CopyRaw(pak, file, fullPath);
                        }
                        catch
                        {
                            File.WriteAllText(fullPath + ".error.txt", ex.ToString());
                        }
                    }
                }
            }

            var manifest = CacheManifest.Create(install);
            manifest.Save(Path.Combine(staging, "manifest.json"));

            progress?.Report(new DecryptProgress("Finalizing cache…", extracted, extracted));

            if (Directory.Exists(AppPaths.CacheRoot))
            {
                Directory.Delete(AppPaths.CacheRoot, true);
            }

            Directory.Move(staging, AppPaths.CacheRoot);
        }
        catch (OperationCanceledException)
        {
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, true); } catch { /* ignore */ }
            }

            throw;
        }
        catch
        {
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, true); } catch { /* ignore */ }
            }

            throw;
        }
    }

    public static async Task<Gbx?> TryOpenScene3dAsync(
        GameInstall install,
        string pakName,
        Int3? size = null,
        CancellationToken cancellationToken = default)
    {
        GbxBootstrap.EnsureInitialized();
        var keys = (await PakList.ParseAsync(install.PackListPath, PakListGame.TM, cancellationToken))
            .ToKeyInfoDictionary();
        var pakPath = Path.Combine(install.PacksFolder, pakName + ".pak");
        if (!File.Exists(pakPath))
        {
            return null;
        }

        try
        {
            await using var pak = keys.TryGetValue(pakName, out var key)
                ? await Pak.ParseAsync(pakPath, key, cancellationToken: cancellationToken)
                : await Pak.ParseAsync(pakPath, cancellationToken: cancellationToken);

            var hint = size is { } s ? $"{s.X}x{s.Z}" : null;
            var files = pak.Files.Values
                .Where(f => f.ClassId == 0x0A003000
                            || f.Name.Contains("Scene3d", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => hint is not null && f.Name.Contains(hint, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var file = files.FirstOrDefault();
            return file is null
                ? null
                : pak.OpenGbxFile(file, new GbxReadSettings { IgnoreExceptionsInBody = true });
        }
        catch
        {
            return null;
        }
    }

    public static async Task<int> RestoreScene3dRawAsync(
        GameInstall install,
        string? pakName = null,
        CancellationToken cancellationToken = default)
    {
        GbxBootstrap.EnsureInitialized();
        var keys = (await PakList.ParseAsync(install.PackListPath, PakListGame.TM, cancellationToken))
            .ToKeyInfoDictionary();

        var restored = 0;
        foreach (var pakPath in Directory.GetFiles(install.PacksFolder, "*.pak"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pakId = Path.GetFileNameWithoutExtension(pakPath);
            if (!string.IsNullOrWhiteSpace(pakName)
                && !string.Equals(pakId, pakName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                await using var pak = keys.TryGetValue(pakId, out var key)
                    ? await Pak.ParseAsync(pakPath, key, cancellationToken: cancellationToken)
                    : await Pak.ParseAsync(pakPath, cancellationToken: cancellationToken);

                foreach (var file in pak.Files.Values)
                {
                    var isScene = file.ClassId == 0x0A003000
                                  || file.FolderPath.IndexOf("Scene3d", StringComparison.OrdinalIgnoreCase) >= 0
                                  || file.Name.IndexOf("Scene3d", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!isScene)
                    {
                        continue;
                    }

                    try
                    {
                        var destDir = Path.Combine(AppPaths.CacheRoot, pakId, file.FolderPath);
                        var destination = Path.Combine(destDir, file.Name);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        CopyRaw(pak, file, destination);
                        var error = destination + ".error.txt";
                        if (File.Exists(error))
                        {
                            File.Delete(error);
                        }

                        if (new FileInfo(destination).Length > 0)
                        {
                            restored++;
                        }
                    }
                    catch
                    {
                        // Keep going so one compressed file cannot block the rest.
                    }
                }
            }
            catch
            {
                // Some paks fail to open; environment extract stays best-effort.
            }
        }

        return restored;
    }

    private static bool ShouldSkip(string fileName)
    {
        return SkippedExtensions.Contains(Path.GetExtension(fileName));
    }

    private static void ExtractFile(Pak pak, PakFile file, string destination)
    {
        using var stream = File.Create(destination);
            try
            {
                if (destination.EndsWith(".Scene3d.Gbx", StringComparison.OrdinalIgnoreCase))
                {
                    CopyRaw(pak, file, stream);
                    return;
                }

                var gbx = pak.OpenGbxFile(file);
                if (gbx.Header is GbxHeaderUnknown)
                {
                    CopyRaw(pak, file, stream);
                    return;
                }

                gbx.Save(stream);
            }
        catch (NotAGbxException)
        {
            CopyRaw(pak, file, stream);
        }
    }

    private static void CopyRaw(Pak pak, PakFile file, string destination)
    {
        using var stream = File.Create(destination);
        CopyRaw(pak, file, stream);
    }

    private static void CopyRaw(Pak pak, PakFile file, Stream stream)
    {
        using var source = pak.OpenFile(file, out _);
        source.CopyTo(stream);
    }
}
