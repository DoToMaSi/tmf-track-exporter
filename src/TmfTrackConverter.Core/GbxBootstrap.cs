using GBX.NET;
using GBX.NET.LZO;

namespace TmfTrackConverter.Core;

public static class GbxBootstrap
{
    private static int _initialized;

    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        Gbx.LZO = new Lzo();
    }
}
