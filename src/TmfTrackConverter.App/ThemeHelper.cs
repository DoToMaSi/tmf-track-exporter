using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TmfTrackConverter.App;

internal static class ThemeHelper
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void Apply(Window window)
    {
        window.Background = (System.Windows.Media.Brush)Application.Current.Resources["BgBrush"];
        window.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextBrush"];

        if (window.IsLoaded)
        {
            TrySetDarkTitleBar(window);
        }
        else
        {
            window.SourceInitialized += (_, _) => TrySetDarkTitleBar(window);
        }
    }

    private static void TrySetDarkTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var useDark = 1;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref useDark, sizeof(int));
        }
    }
}
