using System.Windows.Automation;

namespace SwitchLang;

internal sealed record SelectionSnapshot(string Text, string? Problem = null);

internal sealed class SelectionReader
{
    private Task<SelectionSnapshot>? outstanding;

    internal async Task<SelectionSnapshot> ReadAsync(ForegroundTarget target)
    {
        // A stalled accessibility provider must not freeze the tray or spawn unbounded workers.
        if (outstanding is { IsCompleted: false })
            return new(string.Empty, "Редактор не отвечает на запрос выделения");
        outstanding = Task.Run(() => Read(target));
        try { return await outstanding.WaitAsync(TimeSpan.FromMilliseconds(900)); }
        catch (TimeoutException) { return new(string.Empty, "Редактор не отвечает на запрос выделения"); }
        catch (Exception error)
        {
            DiagnosticLog.Write($"selection_error={error.GetType().Name} hresult={error.HResult:X8}");
            return new(string.Empty, "Не удалось прочитать выделение в редакторе");
        }
    }

    private static SelectionSnapshot Read(ForegroundTarget target)
    {
        if (ForegroundTarget.Read() != target) return new(string.Empty);
        var element = AutomationElement.FocusedElement;
        if (element is null || element.Current.IsPassword) return new(string.Empty);
        // Chromium can expose TextPattern on the document rather than the focused textarea.
        for (var depth = 0; depth < 8 && element is not null; depth++)
        {
            DiagnosticLog.Write($"selection_provider depth={depth} control={element.Current.ControlType.ProgrammaticName} framework={element.Current.FrameworkId} patterns={string.Join(',', element.GetSupportedPatterns().Select(p => p.ProgrammaticName))}");
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var value))
            {
                var ranges = ((TextPattern)value).GetSelection();
                if (ranges.Length != 1) return new(string.Empty);
                // A collapsed range is a caret, not a selection.
                var text = ranges[0].GetText(32769);
                if (text.Length > 32768) return new(string.Empty, "Выделите фрагмент до 32 768 символов");
                return new(text);
            }
            element = TreeWalker.ControlViewWalker.GetParent(element);
        }
        return new(string.Empty, "Это поле не предоставляет доступ к выделенному тексту");
    }
}
