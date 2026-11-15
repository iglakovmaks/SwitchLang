using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Forms;
using SwitchLang;

internal static class Program
{
    private static int result = 1;
    private static Process? app;
    private static readonly IntPtr English = LoadKeyboardLayout("00000409", 0);
    private static readonly IntPtr Russian = LoadKeyboardLayout("00000419", 0);

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var form = new Form { Text = "SwitchLang integration editor", Size = new Size(700, 400) };
        var editor = new TextBox { Multiline = true, Dock = DockStyle.Fill };
        form.Controls.Add(editor);
        form.Shown += async (_, _) =>
        {
            try
            {
                app = Process.Start(new ProcessStartInfo(Path.GetFullPath(args[0]), "--background")
                {
                    UseShellExecute = false
                })!;
                await Task.Delay(1200);
                app.Refresh();
                if (app.MainWindowHandle != IntPtr.Zero)
                    app.CloseMainWindow();
                SetForegroundWindow(form.Handle);
                form.Activate();
                editor.Focus();
                Console.WriteLine($"Editor handle={form.Handle} foreground={GetForegroundWindow()}");
                Check(GetForegroundWindow() == form.Handle, "Editor has foreground focus");
                await SetLayout(form.Handle, English);
                await Task.Delay(500);
                editor.Text = "ghbdtn";
                editor.SelectAll();
                Clipboard.SetText("clipboard-original");
                await SetLayout(form.Handle, Russian);
                await WaitFor(() => editor.Text == "привет", "English → Russian");
                await WaitFor(() => editor.SelectedText == "привет", "Inserted text remains selected");
                await SetLayout(form.Handle, English);
                await WaitFor(() => editor.Text == "ghbdtn", "Russian → English");
                await WaitFor(() => editor.SelectedText == "ghbdtn", "Reverse text remains selected");
                Check(Clipboard.GetText() == "clipboard-original", "Clipboard preserved");
                editor.Select(editor.TextLength, 0);
                await SetLayout(form.Handle, Russian);
                await Task.Delay(600);
                Check(editor.Text == "ghbdtn", "No selection stays untouched");
                Check(Clipboard.GetText() == "clipboard-original", "No selection preserves clipboard");
                editor.Text = "руддщ";
                editor.SelectAll();
                SetKey(NativeMethods.VK_MENU, false);
                SetKey(NativeMethods.VK_SHIFT, false);
                await SetLayout(form.Handle, English);
                await Task.Delay(250);
                Check(editor.Text == "руддщ", "Waits while shortcut modifiers are held");
                SetKey(NativeMethods.VK_SHIFT, true);
                SetKey(NativeMethods.VK_MENU, true);
                await WaitFor(() => editor.Text == "hello", "Converts after modifiers released");
                await Task.Delay(150);
                await SetLayout(form.Handle, Russian);
                await WaitFor(() => editor.Text == "руддщ", "Repeated reverse conversion");
                await TestNotepad();
                result = 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                using var screenshot = new Bitmap(Screen.PrimaryScreen!.Bounds.Width, Screen.PrimaryScreen.Bounds.Height);
                using var graphics = Graphics.FromImage(screenshot);
                graphics.CopyFromScreen(0, 0, 0, 0, screenshot.Size);
                screenshot.Save("artifacts/editor-failure.png");
            }
            finally
            {
                if (app is { HasExited: false }) app.Kill(true);
                form.Close();
            }
        };
        Application.Run(form);
        return result;
    }

    private static async Task TestNotepad()
    {
        using var notepad = Process.Start("notepad.exe")!;
        try
        {
            await WaitFor(() => { notepad.Refresh(); return notepad.MainWindowHandle != IntPtr.Zero; }, "Notepad opened");
            var handle = notepad.MainWindowHandle;
            SetForegroundWindow(handle);
            await Task.Delay(500);
            var root = AutomationElement.FromHandle(handle);
            var edit = root.FindAll(TreeScope.Descendants, new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document)))
                .Cast<AutomationElement>()
                .Where(e => e.Current.IsKeyboardFocusable && !e.Current.IsOffscreen)
                .OrderByDescending(e => e.Current.BoundingRectangle.Width * e.Current.BoundingRectangle.Height)
                .FirstOrDefault();
            Check(edit is not null, "Notepad editor found");
            edit!.SetFocus();
            await SetLayout(handle, English);
            await Task.Delay(500);
            NativeMethods.SendUnicodeText("ghbdtn");
            await Task.Delay(150);
            NativeMethods.SendCtrlKey(0x41);
            await Task.Delay(100);
            await SetLayout(handle, Russian);
            var text = (TextPattern)edit.GetCurrentPattern(TextPattern.Pattern);
            await WaitFor(() => text.DocumentRange.GetText(-1).TrimEnd('\r', '\n') == "привет", "Real Notepad English → Russian");
            await Task.Delay(150);
            await SetLayout(handle, English);
            await WaitFor(() => text.DocumentRange.GetText(-1).TrimEnd('\r', '\n') == "ghbdtn", "Real Notepad Russian → English");
        }
        finally { if (!notepad.HasExited) notepad.Kill(true); }
    }

    private static void SetKey(ushort key, bool up)
    {
        var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD,
            union = new NativeMethods.InputUnion { keyboardInput = new NativeMethods.KEYBDINPUT {
                virtualKey = key, flags = up ? NativeMethods.KEYEVENTF_KEYUP : 0
            }}};
        Check(NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>()) == 1, "Key input accepted");
    }

    private static async Task SetLayout(IntPtr window, IntPtr layout)
    {
        Check(layout != IntPtr.Zero, "Keyboard layout installed");
        PostMessage(window, 0x0050, IntPtr.Zero, layout);
        await WaitFor(() => GetKeyboardLayout(GetWindowThreadProcessId(window, out _)) == layout,
            "Layout changed");
    }

    private static async Task WaitFor(Func<bool> predicate, string label)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 5000)
        {
            if (predicate()) { Console.WriteLine($"PASS: {label}"); return; }
            await Task.Delay(30);
        }
        throw new Exception($"FAIL: {label}; foreground={GetForegroundWindow()}");
    }

    private static void Check(bool success, string label)
    {
        if (!success) throw new Exception($"FAIL: {label}");
        Console.WriteLine($"PASS: {label}");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadKeyboardLayout(string name, uint flags);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);
}
