using System.Windows;
using TmfTrackConverter.Core;

namespace TmfTrackConverter.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        GbxBootstrap.EnsureInitialized();
        AppPaths.EnsureRoot();
        base.OnStartup(e);
    }
}
