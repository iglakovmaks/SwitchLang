using System.Windows.Forms;

namespace SwitchLang;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, @"Local\SwitchLang.Resident", out var firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("SwitchLang уже запущен. Откройте его значок в системном трее.", "SwitchLang");
            return;
        }
        Application.Run(new SwitchLangContext(args.Contains("--background")));
    }
}
