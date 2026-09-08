using Microsoft.Win32;
using System.Windows.Forms;

namespace SwitchLang;

internal sealed class SwitchLangContext : ApplicationContext
{
    private readonly NotifyIcon notifyIcon;
    private readonly System.Windows.Forms.Timer layoutTimer;
    private readonly ToolStripMenuItem toggleItem;
    private KeyboardLayout? previousLayout;
    private Form? welcomeForm;
    private bool enabled = true;
    private bool isConverting;

    public SwitchLangContext()
    {
        previousLayout = CurrentLayout();

        toggleItem = new ToolStripMenuItem("Автоисправление")
        {
            Checked = true,
            CheckOnClick = true
        };
        toggleItem.Click += (_, _) =>
        {
            enabled = toggleItem.Checked;
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Как пользоваться", null, (_, _) => ShowHelp());
        menu.Items.Add("Добавить в автозапуск", null, (_, _) => InstallAutostart());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выйти", null, (_, _) => ExitThread());

        notifyIcon = new NotifyIcon
        {
            Icon = IconLoader.Load(),
            Text = "SwitchLang",
            ContextMenuStrip = menu,
            Visible = true
        };
        notifyIcon.DoubleClick += (_, _) => ShowWelcome();

        layoutTimer = new System.Windows.Forms.Timer { Interval = 120 };
        layoutTimer.Tick += OnLayoutTick;
        layoutTimer.Start();
        Application.Idle += ShowWelcomeOnIdle;
    }

    private void OnLayoutTick(object? sender, EventArgs e)
    {
        var current = CurrentLayout();
        if (current is null)
        {
            return;
        }

        if (previousLayout is null)
        {
            previousLayout = current;
            return;
        }

        if (current == previousLayout)
        {
            return;
        }

        var sourceLayout = previousLayout.Value;
        previousLayout = current;
        if (!enabled || isConverting)
        {
            return;
        }

        isConverting = true;
        try
        {
            var result = ReplaceSelectedText(sourceLayout);
            if (result.Success)
            {
                notifyIcon.Text = "SwitchLang: исправлено";
            }
        }
        catch (Exception error)
        {
            notifyIcon.Text = ShortStatus(error.Message);
        }
        finally
        {
            isConverting = false;
        }
    }

    private static KeyboardLayout? CurrentLayout()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return null;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var hkl = NativeMethods.GetKeyboardLayout(threadId);
        var languageId = unchecked((ushort)hkl.ToInt64());
        var primaryLanguage = languageId & 0x03ff;
        return primaryLanguage switch
        {
            0x19 => KeyboardLayout.Russian,
            0x09 => KeyboardLayout.English,
            _ => null
        };
    }

    private static ConversionResult ReplaceSelectedText(KeyboardLayout sourceLayout)
    {
        IDataObject? originalClipboard = null;
        try
        {
            originalClipboard = Clipboard.GetDataObject();
            Clipboard.Clear();
            NativeMethods.SendCtrlKey((ushort)'C');
            Thread.Sleep(90);

            var selected = Clipboard.ContainsText(TextDataFormat.UnicodeText)
                ? Clipboard.GetText(TextDataFormat.UnicodeText)
                : string.Empty;
            if (string.IsNullOrEmpty(selected) || !KeyboardLayoutConverter.CanConvert(selected, sourceLayout))
            {
                return ConversionResult.NotChanged("Выделенный текст не найден");
            }

            var converted = KeyboardLayoutConverter.Convert(selected, sourceLayout);
            if (converted == selected)
            {
                return ConversionResult.NotChanged("В выделении нет символов для преобразования");
            }

            Clipboard.SetText(converted, TextDataFormat.UnicodeText);
            NativeMethods.SendCtrlKey((ushort)'V');
            Thread.Sleep(90);

            // Re-select the inserted text so another layout switch converts it back.
            NativeMethods.SelectPreviousCharacters(converted.Length);
            return ConversionResult.Changed($"Исправлено символов: {selected.Length}");
        }
        finally
        {
            if (originalClipboard is not null)
            {
                try
                {
                    Clipboard.SetDataObject(originalClipboard, true);
                }
                catch
                {
                    // Clipboard restoration must not crash the resident app.
                }
            }
        }
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
            Text = "SwitchLang",
            ClientSize = new Size(520, 330),
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
            Text = "В Windows отдельное разрешение Accessibility обычно не требуется. Если Windows покажет запрос на доступ к буферу обмена, разрешите его.",
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

        var credit = new Label
        {
            Text = "Developed by iglakovmaks",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(28, 288)
        };
        welcomeForm.Controls.Add(credit);

        var close = new Button
        {
            Text = "Понятно",
            DialogResult = DialogResult.OK,
            Size = new Size(100, 32),
            Location = new Point(392, 282)
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
        const int maxLength = 60;
        return message.Length <= maxLength ? $"SwitchLang: {message}" : $"SwitchLang: {message[..maxLength]}…";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            layoutTimer.Stop();
            layoutTimer.Dispose();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            welcomeForm?.Close();
        }
        base.Dispose(disposing);
    }

    private readonly record struct ConversionResult(bool Success, string Details)
    {
        public static ConversionResult Changed(string details) => new(true, details);
        public static ConversionResult NotChanged(string details) => new(false, details);
    }
}
