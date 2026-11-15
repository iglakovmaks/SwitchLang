using Accessibility;
using System.Runtime.InteropServices;

namespace SwitchLang;

internal static class BrowserSelection
{
    // Chromium and Firefox also expose IAccessible2. Querying IA2 opts into their
    // full accessibility tree without changing browser settings or using the clipboard.
    internal static SelectionSnapshot Read(ForegroundTarget target)
    {
        var iid = typeof(IAccessible).GUID;
        var hr = AccessibleObjectFromWindow(target.Focus, unchecked((uint)-4), ref iid, out var root);
        if (hr < 0 || root is null) return Unavailable();
        if (root is IServiceProvider services)
        {
            var ia2 = new Guid("E89F726E-C4F4-4C19-BB19-B647D7FA8478");
            services.QueryService(ref ia2, ref ia2, out _);
        }
        var accessible = root;
        for (var depth = 0; depth < 12; depth++)
        {
            if (accessible.accFocus is IAccessible child && !ReferenceEquals(child, accessible))
            {
                accessible = child;
                continue;
            }
            break;
        }
        var role = System.Convert.ToInt32(accessible.get_accRole(0));
        var state = System.Convert.ToInt32(accessible.get_accState(0));
        DiagnosticLog.Write($"browser_accessible role={role} protected={(state & 0x20000000) != 0}");
        if ((state & (0x20000000 | 0x40)) != 0) return new(string.Empty);
        if (role != 42 || accessible is not IServiceProvider provider) return Unavailable();
        var service = typeof(IAccessible).GUID;
        var textId = typeof(IAccessibleText).GUID;
        hr = provider.QueryService(ref service, ref textId, out var value);
        if (hr < 0 || value is not IAccessibleText text) return Unavailable();
        if (text.GetSelectionCount(out var count) < 0 || count != 1) return new(string.Empty);
        if (text.GetSelection(0, out var start, out var end) < 0 || start == end) return new(string.Empty);
        if (Math.Abs((long)end - start) > 32768) return new(string.Empty, "Выделите фрагмент до 32 768 символов");
        hr = text.GetText(Math.Min(start, end), Math.Max(start, end), out var selected);
        return hr >= 0 ? new(selected ?? string.Empty) : Unavailable();
    }

    private static SelectionSnapshot Unavailable() => new(string.Empty, "Браузер ещё не предоставил доступ к выделению");

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible? accessible);

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object? value);
    }

    // ABI order from https://github.com/LinuxA11y/IAccessible2/blob/master/api/AccessibleText.idl
    [ComImport, Guid("24FD2FFB-3AAD-4A08-8335-A3AD89C0FB4B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAccessibleText
    {
        [PreserveSig] int AddSelection(int start, int end);
        [PreserveSig] int GetAttributes(int offset, out int start, out int end, [MarshalAs(UnmanagedType.BStr)] out string attributes);
        [PreserveSig] int GetCaretOffset(out int offset);
        [PreserveSig] int GetCharacterExtents(int offset, int coordinateType, out int x, out int y, out int width, out int height);
        [PreserveSig] int GetSelectionCount(out int count);
        [PreserveSig] int GetOffsetAtPoint(int x, int y, int coordinateType, out int offset);
        [PreserveSig] int GetSelection(int index, out int start, out int end);
        [PreserveSig] int GetText(int start, int end, [MarshalAs(UnmanagedType.BStr)] out string? text);
    }
}
