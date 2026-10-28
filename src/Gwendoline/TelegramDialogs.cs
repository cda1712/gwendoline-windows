using System.Diagnostics;
using Microsoft.Win32;

namespace Gwendoline;

/// <summary>The two windows of the Telegram setup: paste the token, then tap Start in the bot chat.</summary>
internal static class TelegramDialogs
{
    /// <summary>Asks for the bot token. Returns it, or null when cancelled.</summary>
    public static string? AskToken()
    {
        using var form = Window("Connect Telegram", 460);
        var steps = Label(
            "1. Click Open BotFather, send /newbot and follow the steps.\n"
            + "2. Paste the token BotFather gives you below.\n"
            + "3. Tap Start in the chat with your bot.\n\n"
            + "Use a new bot for each PC.");
        var field = new TextBox { Dock = DockStyle.Top, PlaceholderText = "123456789:AAH...", Margin = new Padding(0, 8, 0, 8) };
        var connect = new Button { Text = "Connect", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var botFather = new Button { Text = "Open BotFather", AutoSize = true };
        botFather.Click += (_, _) => Open("tg://resolve?domain=BotFather", "https://t.me/BotFather");
        form.AcceptButton = connect;
        form.CancelButton = cancel;
        Layout(form, [steps, field], [connect, cancel, botFather]);
        form.Shown += (_, _) => field.Focus();
        return form.ShowDialog() == DialogResult.OK ? field.Text : null;
    }

    /// <summary>Opens the bot chat with the Start link, and shows the link for a phone.</summary>
    public static void ShowStart(string bot, (string App, string Web) links)
    {
        Open(links.App, links.Web);
        using var form = Window("Tap Start in Telegram", 460);
        var text = Label(
            $"Telegram opens the chat with @{bot}. Tap Start, and Gwendoline answers that it is connected.\n\n"
            + "Telegram is on your phone only? Send yourself this link and open it there.");
        var link = new TextBox { Dock = DockStyle.Top, ReadOnly = true, Text = links.Web, Margin = new Padding(0, 8, 0, 8) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var copy = new Button { Text = "Copy Link", AutoSize = true };
        copy.Click += (_, _) => Clipboard.SetText(links.Web);
        form.AcceptButton = ok;
        Layout(form, [text, link], [ok, copy]);
        form.ShowDialog();
    }

    /// <summary>Opens Telegram Desktop directly when it is installed, the web link otherwise.</summary>
    public static void Open(string app, string web)
    {
        using var handler = Registry.ClassesRoot.OpenSubKey(@"tg\shell\open\command");
        Process.Start(new ProcessStartInfo(handler != null ? app : web) { UseShellExecute = true })?.Dispose();
    }

    private static Form Window(string title, int width)
    {
        return new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = true,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16),
            Font = SystemFonts.MessageBoxFont,
            MinimumSize = new Size(width, 0),
        };
    }

    private static Label Label(string text)
    {
        return new Label { Text = text, AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(0, 0, 0, 4) };
    }

    private static void Layout(Form form, Control[] body, Button[] buttons)
    {
        var column = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Dock = DockStyle.Fill,
        };
        foreach (var control in body)
        {
            if (control is TextBox box)
            {
                box.Dock = DockStyle.None;
                box.Width = 420;
            }
            column.Controls.Add(control);
        }
        var row = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Width = 420, Margin = new Padding(0, 8, 0, 0) };
        row.Controls.AddRange(buttons);
        column.Controls.Add(row);
        form.Controls.Add(column);
    }
}
