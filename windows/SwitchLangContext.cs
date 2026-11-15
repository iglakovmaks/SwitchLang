using Microsoft.Win32;
using System.Windows.Forms;

namespace SwitchLang;

internal sealed class SwitchLangContext : ApplicationContext
{
    private readonly NotifyIcon notifyIcon;
    private readonly ConversionController controller;
    private readonly ToolStripMenuItem toggleItem;
    private Form? welcomeForm;
    private Label? statusLabel;
    private string status = "Включено — выделите текст и смените раскладку";

    public SwitchLangContext(bool background = false)
    {
        toggleItem = new ToolStripMenuItem("Автоисправление")
        {
            Checked = true,
            CheckOnClick = true
        };
        controller = new ConversionController();
        controller.StatusChanged = UpdateStatus;
        toggleItem.Click += (_, _) => controller.SetEnabled(toggleItem.Checked);
        var menu = new ContextMenuStrip();
        menu.Items.Add(toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Как пользоваться", null, (_, _) => ShowWelcome());
        menu.Items.Add("Открыть журнал диагностики", null, (_, _) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", DiagnosticLog.PathName)
                { UseShellExecute = true }));
        menu.Items.Add("Добавить в автозапуск", null, (_, _) => InstallAutostart());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выйти", null, (_, _) => ExitThread());
        notifyIcon = new NotifyIcon
        {
            Icon = IconLoader.Load(),
            Text = "SwitchLang 0.2.3",
            ContextMenuStrip = menu,
            Visible = true
        };
        notifyIcon.DoubleClick += (_, _) => ShowWelcome();
        if (!background) Application.Idle += ShowWelcomeOnIdle;
        DiagnosticLog.Write($"started version=0.2.3 architecture={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
    }

    private void UpdateStatus(string message)
    {
        status = message;
        notifyIcon.Text = ShortStatus(message);
        if (statusLabel is { IsDisposed: false }) statusLabel.Text = message;
        DiagnosticLog.Write($"status={message}");
    }

    private void ShowWelcomeOnIdle(object? sender, EventArgs e)
    {
        Application.Idle -= ShowWelcomeOnIdle;
        ShowWelcome();
    }

    private void ShowWelcome()
    {
        if (welcomeForm is { IsDisposed: false })
        {
            welcomeForm.Show();
            welcomeForm.Activate();
            return;
        }

        welcomeForm = new Form
        {
            Text = "SwitchLang 0.2.3",
            ClientSize = new Size(520, 382),
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = true,
            Icon = IconLoader.Load()
        };

        var title = new Label
        {
            Text = "Добро пожаловать в SwitchLang",
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(28, 24)
        };
        welcomeForm.Controls.Add(title);

        var instructions = new Label
        {
            Text = "Выделите текст, набранный в неправильной раскладке, и нажмите обычное системное сочетание переключения языка.\n\nSwitchLang заметит RU ↔ EN, исправит выделение и оставит его выделенным, поэтому переключение можно повторить обратно.",
            AutoSize = false,
            Size = new Size(458, 104),
            Location = new Point(28, 66)
        };
        welcomeForm.Controls.Add(instructions);

        var permissions = new Label
        {
            Text = "Для обычных редакторов отдельные разрешения не нужны. Если редактор открыт от имени администратора, SwitchLang тоже потребуются права администратора.",
            AutoSize = false,
            Size = new Size(458, 54),
            Location = new Point(28, 178)
        };
        welcomeForm.Controls.Add(permissions);

        var note = new Label
        {
            Text = "Вы можете закрыть это окно — приложение продолжит работать в фоновом режиме.",
            AutoSize = false,
            Size = new Size(458, 30),
            Location = new Point(28, 238)
        };
        welcomeForm.Controls.Add(note);

        statusLabel = new Label
        {
            Text = status,
            AutoSize = false,
            Size = new Size(458, 48),
            Location = new Point(28, 278),
            ForeColor = SystemColors.HotTrack
        };
        welcomeForm.Controls.Add(statusLabel);

        var credit = new Label
        {
            Text = "Developed by iglakovmaks",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(28, 344)
        };
        welcomeForm.Controls.Add(credit);

        var close = new Button
        {
            Text = "Понятно",
            DialogResult = DialogResult.OK,
            Size = new Size(100, 32),
            Location = new Point(392, 334)
        };
        close.Click += (_, _) => welcomeForm.Close();
        welcomeForm.AcceptButton = close;
        welcomeForm.Controls.Add(close);
        welcomeForm.FormClosed += (_, _) => welcomeForm = null;
        welcomeForm.Show();
    }

    private void ShowHelp()
    {
        ShowWelcome();
    }

    private static void InstallAutostart()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException("Не удалось определить путь приложения");
        }

        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            writable: true) ?? Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run");
        key.SetValue("SwitchLang", $"\"{executable}\" --background");
        MessageBox.Show("Автозапуск включён.", "SwitchLang", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string ShortStatus(string message)
    {
        const int maxLength = 48;
        return message.Length <= maxLength ? $"SwitchLang: {message}" : $"SwitchLang: {message[..maxLength]}…";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            controller.Dispose();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            welcomeForm?.Close();
        }
        base.Dispose(disposing);
    }

}
