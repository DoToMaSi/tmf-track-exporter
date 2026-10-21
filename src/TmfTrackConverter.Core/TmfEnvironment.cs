using GBX.NET;

namespace TmfTrackConverter.Core;

public enum TmfEnvironment
{
    Stadium,
    Island,
    Desert,
    Rally,
    Bay,
    Coast,
    Alpine
}

public static class TmfEnvironments
{
    public static readonly IReadOnlyList<TmfEnvironment> All = Enum.GetValues<TmfEnvironment>();

    public static string PakName(this TmfEnvironment environment) => environment switch
    {
        TmfEnvironment.Stadium => "Stadium",
        TmfEnvironment.Island => "Island",
        TmfEnvironment.Desert => "Speed",
        TmfEnvironment.Rally => "Rally",
        TmfEnvironment.Bay => "Bay",
        TmfEnvironment.Coast => "Coast",
        TmfEnvironment.Alpine => "Alpine",
        _ => environment.ToString()
    };

    public static string DisplayName(this TmfEnvironment environment) => environment switch
    {
        TmfEnvironment.Desert => "Desert (Speed)",
        TmfEnvironment.Alpine => "Alpine (Snow)",
        _ => environment.ToString()
    };

    public static bool TryParse(string? collection, out TmfEnvironment environment)
    {
        environment = default;
        if (string.IsNullOrWhiteSpace(collection))
        {
            return false;
        }

        switch (collection.Trim())
        {
            case "Stadium":
                environment = TmfEnvironment.Stadium;
                return true;
            case "Island":
                environment = TmfEnvironment.Island;
                return true;
            case "Speed":
            case "Desert":
                environment = TmfEnvironment.Desert;
                return true;
            case "Rally":
                environment = TmfEnvironment.Rally;
                return true;
            case "Bay":
                environment = TmfEnvironment.Bay;
                return true;
            case "Coast":
                environment = TmfEnvironment.Coast;
                return true;
            case "Alpine":
            case "Snow":
                environment = TmfEnvironment.Alpine;
                return true;
            default:
                return false;
        }
    }

    public static TmfEnvironment? FromCollection(Id? collection)
    {
        return collection is null ? null : TryParse(collection.Value.ToString(), out var env) ? env : null;
    }

    public static Int3 GetBlockSize(this TmfEnvironment environment)
    {
        return new Id(environment switch
        {
            TmfEnvironment.Desert => "Desert",
            TmfEnvironment.Alpine => "Snow",
            _ => environment.ToString()
        }).GetBlockSize();
    }
}
