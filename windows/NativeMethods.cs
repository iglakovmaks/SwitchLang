using System.Runtime.InteropServices;

namespace SwitchLang;

internal static class NativeMethods
{
    internal const ushort VK_CONTROL = 0x11;
    internal const ushort VK_SHIFT = 0x10;
    internal const ushort VK_LEFT = 0x25;
    internal const uint INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInput);

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        internal uint type;
        internal InputUnion union;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        internal KEYBDINPUT keyboardInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        internal ushort virtualKey;
        internal ushort scanCode;
        internal uint flags;
        internal uint time;
        internal UIntPtr extraInfo;
    }

    internal static void SendCtrlKey(ushort key)
    {
        SendKeys(new[]
        {
            (VK_CONTROL, false),
            (key, false),
            (key, true),
            (VK_CONTROL, true)
        });
    }

    internal static void SelectPreviousCharacters(int count)
    {
        if (count <= 0)
        {
            return;
        }

        var events = new List<(ushort Key, bool KeyUp)> { (VK_SHIFT, false) };
        for (var index = 0; index < count; index++)
        {
            events.Add((VK_LEFT, false));
            events.Add((VK_LEFT, true));
        }
        events.Add((VK_SHIFT, true));
        SendKeys(events);
    }

    private static void SendKeys(IEnumerable<(ushort Key, bool KeyUp)> keys)
    {
        var inputs = keys.Select(key => new INPUT
        {
            type = INPUT_KEYBOARD,
            union = new InputUnion
            {
                keyboardInput = new KEYBDINPUT
                {
                    virtualKey = key.Key,
                    scanCode = 0,
                    flags = key.KeyUp ? KEYEVENTF_KEYUP : 0,
                    time = 0,
                    extraInfo = UIntPtr.Zero
                }
            }
        }).ToArray();

        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
        {
            throw new InvalidOperationException("Windows не смог отправить клавиатурное событие");
        }
    }
}
