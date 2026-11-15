using System.IO;

namespace SwitchLang;

internal static class DiagnosticLog
{
    internal static string PathName { get; } = Environment.GetEnvironmentVariable("SWITCHLANG_LOG") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwitchLang", "diagnostics.log");

    internal static void Write(string message)
    {
        // Do not log selected text, clipboard data, window titles or document paths.
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            if (File.Exists(PathName) && new FileInfo(PathName).Length > 512_000)
                File.WriteAllText(PathName, string.Empty);
            File.AppendAllText(PathName, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch { /* Diagnostics must not interrupt editing. */ }
    }
}
