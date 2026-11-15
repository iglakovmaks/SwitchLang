using System.Globalization;

namespace SwitchLang;

internal sealed class ConversionController : IDisposable
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 60 };
    private readonly SelectionReader selection = new();
    private ForegroundTarget? previous;
    private Pending? pending;
    private bool busy;
    private bool disposed;
    private bool enabled = true;
    internal Action<string>? StatusChanged { get; set; }

    internal ConversionController()
    {
        timer.Tick += OnTick;
        timer.Start();
    }

    internal void SetEnabled(bool value)
    {
        enabled = value;
        pending = null;
        StatusChanged?.Invoke(value ? "Включено — выделите текст и смените раскладку" : "Выключено");
    }

    private async void OnTick(object? sender, EventArgs args)
    {
        if (busy || disposed) return;
        var target = ForegroundTarget.Read();
        if (target is null)
        {
            previous = null;
            pending = null;
            return;
        }
        var current = target.Value;
        if (previous is not { } old || current.Window != old.Window || current.Focus != old.Focus)
        {
            previous = current;
            pending = null;
            DiagnosticLog.Write($"focus thread={current.Thread} layout={current.Layout}");
            return;
        }
        previous = current;
        if (current.Layout != old.Layout)
        {
            pending = enabled ? new(current, old.Layout, Environment.TickCount64) : null;
            DiagnosticLog.Write($"layout {old.Layout}->{current.Layout}");
        }
        if (pending is not { } work || !enabled) return;
        var age = Environment.TickCount64 - work.Created;
        if (age > 2500) { pending = null; return; }
        if (age < 100 || NativeMethods.IsModifierKeyDown()) return;
        pending = null;
        busy = true;
        try
        {
            var selected = await selection.ReadAsync(work.Target);
            if (disposed || !enabled || ForegroundTarget.Read() != work.Target) return;
            if (selected.Problem is { } problem) { Report(problem); return; }
            if (selected.Text.Length == 0) { DiagnosticLog.Write("no_selection"); return; }
            var text = KeyboardLayoutConverter.Convert(selected.Text, work.Source);
            if (text == selected.Text) return;
            // Do not send editing keys if the user has started another shortcut while UIA was reading.
            if (NativeMethods.IsModifierKeyDown()) return;
            NativeMethods.SendUnicodeText(text);
            // Yield to the target application's input queue, rather than blocking its clipboard requests.
            await Task.Delay(80);
            if (disposed || ForegroundTarget.Read() != work.Target || NativeMethods.IsModifierKeyDown()) return;
            var count = StringInfo.ParseCombiningCharacters(text.Replace("\r\n", "\n")).Length;
            NativeMethods.SelectPreviousCharacters(count);
            Report("Текст исправлен");
            DiagnosticLog.Write($"converted characters={text.Length}");
        }
        catch (Exception error)
        {
            DiagnosticLog.Write($"conversion_error={error.GetType().Name} hresult={error.HResult:X8}");
            Report(error.Message);
        }
        finally { busy = false; }
    }

    private void Report(string message) => StatusChanged?.Invoke(message);

    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        timer.Dispose();
    }

    private readonly record struct Pending(ForegroundTarget Target, KeyboardLayout Source, long Created);
}
