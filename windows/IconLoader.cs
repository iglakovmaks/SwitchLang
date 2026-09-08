using System.Drawing;
using System.Runtime.InteropServices;

namespace SwitchLang;

internal static class IconLoader
{
    public static Icon Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "icon.png");
        if (!File.Exists(path))
        {
            return (Icon)SystemIcons.Application.Clone();
        }

        using var bitmap = new Bitmap(path);
        var handle = bitmap.GetHicon();
        using var nativeIcon = Icon.FromHandle(handle);
        var copy = (Icon)nativeIcon.Clone();
        DestroyIcon(handle);
        return copy;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
