using System.Text;

namespace TmfTrackConverter.Core;

public sealed class GameInstall
{
    public required string Folder { get; init; }

    public required string ExecutablePath { get; init; }

    public required string PacksFolder { get; init; }

    public required string PackListPath { get; init; }

    public string GameDataFolder => Path.Combine(Folder, "GameData");

    public GameEdition Edition { get; init; }

    public IReadOnlyList<TmfEnvironment> AvailableEnvironments { get; init; } = [];

    public string UserSubDir { get; init; } = "TrackMania";

    public string UserDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        UserSubDir);

    public string DefaultChallengesFolder => Path.Combine(UserDataFolder, "Tracks", "Challenges");

    public static bool TryDetect(string? folder, out GameInstall? install, out string error)
    {
        install = null;
        error = "";

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            error = "The selected folder does not exist.";
            return false;
        }

        var exe = Path.Combine(folder, "TmForever.exe");
        if (!File.Exists(exe))
        {
            error = "TmForever.exe was not found in that folder.";
            return false;
        }

        var packs = Path.Combine(folder, "Packs");
        var packList = Path.Combine(packs, "packlist.dat");
        if (!File.Exists(packList))
        {
            error = "Packs\\packlist.dat was not found. This does not look like a TrackMania Forever install.";
            return false;
        }

        var available = new List<TmfEnvironment>();
        foreach (var env in TmfEnvironments.All)
        {
            if (File.Exists(Path.Combine(packs, env.PakName() + ".pak")))
            {
                available.Add(env);
            }
        }

        var edition = available.Count > 1 ? GameEdition.United
            : available.Contains(TmfEnvironment.Stadium) ? GameEdition.Nations
            : GameEdition.Unknown;

        install = new GameInstall
        {
            Folder = Path.GetFullPath(folder),
            ExecutablePath = exe,
            PacksFolder = packs,
            PackListPath = packList,
            Edition = edition,
            AvailableEnvironments = available,
            UserSubDir = ReadUserSubDir(folder)
        };
        return true;
    }

    public bool Supports(TmfEnvironment environment) => AvailableEnvironments.Contains(environment);

    private static string ReadUserSubDir(string folder)
    {
        foreach (var iniName in new[] { "Nadeo.ini", "STEAM_Nadeo.ini" })
        {
            var iniPath = Path.Combine(folder, iniName);
            if (!File.Exists(iniPath))
            {
                continue;
            }

            foreach (var line in File.ReadLines(iniPath, Encoding.Latin1))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("UserSubDir=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmed["UserSubDir=".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
        }

        return "TrackMania";
    }
}
