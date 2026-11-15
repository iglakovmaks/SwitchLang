using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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
                Check(editor.SelectedText == "привет", "Inserted text remains selected");
                await SetLayout(form.Handle, English);
                await WaitFor(() => editor.Text == "ghbdtn", "Russian → English");
                Check(Clipboard.GetText() == "clipboard-original", "Clipboard preserved");
                editor.Select(editor.TextLength, 0);
                await SetLayout(form.Handle, Russian);
                await Task.Delay(600);
                Check(editor.Text == "ghbdtn", "No selection stays untouched");
                result = 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); }
            finally
            {
                if (app is { HasExited: false }) app.Kill(true);
                form.Close();
            }
        };
        Application.Run(form);
        return result;
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
