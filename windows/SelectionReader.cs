using UIA = Interop.UIAutomationClient;

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
        // Native UIA3 is required for Chromium; the older managed UIA2 bridge can
        // report every browser node as a Pane without any text/selection patterns.
        UIA.IUIAutomation automation = new UIA.CUIAutomation8();
        var timeouts = (UIA.IUIAutomation2)automation;
        timeouts.ConnectionTimeout = 500;
        timeouts.TransactionTimeout = 500;
        var element = automation.GetFocusedElement();
        if (element is null || element.CurrentIsPassword != 0) return new(string.Empty);
        var isBrowser = element.CurrentFrameworkId == "Chrome" || element.CurrentFrameworkId == "Mozilla";
        // Chromium can expose TextPattern on the document rather than the focused textarea.
        for (var depth = 0; depth < 8 && element is not null; depth++)
        {
            DiagnosticLog.Write($"selection_provider depth={depth} control={element.CurrentControlType} framework={element.CurrentFrameworkId}");
            if (element.GetCurrentPattern(UIA.UIA_PatternIds.UIA_TextPatternId) is UIA.IUIAutomationTextPattern pattern)
            {
                var ranges = pattern.GetSelection();
                if (ranges.Length != 1) return new(string.Empty);
                // A collapsed range is a caret, not a selection.
                var text = ranges.GetElement(0).GetText(32769);
                if (text.Length > 32768) return new(string.Empty, "Выделите фрагмент до 32 768 символов");
                return new(text);
            }
            element = automation.ControlViewWalker.GetParentElement(element);
        }
        return isBrowser ? BrowserSelection.Read(target) :
            new(string.Empty, "Это поле не предоставляет доступ к выделенному тексту");
    }
}
