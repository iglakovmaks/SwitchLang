using System.Runtime.InteropServices;

namespace SwitchLang;

internal readonly record struct ForegroundTarget(IntPtr Window, IntPtr Focus, uint Thread, KeyboardLayout Layout)
{
    internal static ForegroundTarget? Read()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        var thread = NativeMethods.GetWindowThreadProcessId(window, out var process);
        if (process == Environment.ProcessId) return null;
        var info = new NativeMethods.GUITHREADINFO { size = (uint)Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        if (!NativeMethods.GetGUIThreadInfo(thread, ref info) || info.focus == IntPtr.Zero) return null;
        // Browser/editor focus can belong to a different GUI thread than the top-level frame.
        var focusThread = NativeMethods.GetWindowThreadProcessId(info.focus, out _);
        var language = (ushort)NativeMethods.GetKeyboardLayout(focusThread).ToInt64() & 0x03ff;
        KeyboardLayout? layout = language switch
        {
            0x19 => KeyboardLayout.Russian,
            0x09 => KeyboardLayout.English,
            _ => null
        };
        return layout is { } value ? new(window, info.focus, focusThread, value) : null;
    }
}
