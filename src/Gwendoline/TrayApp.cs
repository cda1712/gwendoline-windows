using Microsoft.Win32;
using Gwendoline.Core;
using Timer = System.Windows.Forms.Timer;

namespace Gwendoline;

/// <summary>
/// The tray icon, its menu, a check every 5 seconds and the way out. Everything runs on the UI thread,
/// and every power change is a quick synchronous call, so two transitions never overlap.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon tray = new();
    private readonly EmojiIcon icons = new();
    private readonly PowerWindow window = new();
    private readonly TelegramBot telegram = new();
    private readonly Safety safety = new();
    private readonly Notifier notifier = new();
    private readonly Timer timer = new() { Interval = 5000 };
    /// <summary>The last lid change worked and was confirmed.</summary>
    private bool changeAllowed = true;
    /// <summary>While a closing message is on its way, a closed PC waits for it before sleeping, <see cref="sleepHoldLimit"/> at most.</summary>
    private DateTime? sleepHeldUntil;
    private static readonly TimeSpan sleepHoldLimit = TimeSpan.FromSeconds(10);
    private bool isTerminating;
    /// <summary><see cref="TelegramText.CouldNotTurnOff"/> goes out once per session, not on every check.</summary>
    private bool warnedCouldNotTurnOff;
    /// <summary>Gwendoline closed the session during this check: Telegram is already told.</summary>
    private bool closedThisCheck;

    /// <summary>
    /// <c>reg add HKCU\Software\Gwendoline /v BatteryFloor /t REG_DWORD /d 10 /f</c>, read on every check.
    /// </summary>
    private static Policy Policy
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Gwendoline");
            return new Policy(key?.GetValue("BatteryFloor") is int floor ? floor : Policy.DefaultBatteryFloor);
        }
    }

    public TrayApp()
    {
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Toggle();
            }
        };
        tray.ContextMenuStrip = new ContextMenuStrip();
        tray.ContextMenuStrip.Opening += (_, e) => BuildMenu(tray.ContextMenuStrip, e);
        window.Woke += () => safety.DidWake();
        window.SessionEnding += SessionEnding;
        timer.Tick += (_, _) => CheckState();
        SystemEvents.UserPreferenceChanged += ThemeChanged;
        SystemEvents.DisplaySettingsChanged += ThemeChanged;
        Start();
    }

    private PowerState Read() => SystemReadings.Current(window);

    // Lifecycle

    private void Start()
    {
        // Fail safe: every launch gives sleep back, so a crash or a restart (the lid setting survives
        // reboots) never leaves the PC stuck awake.
        var actions = safety.Launch(Read());
        Perform(actions.Where(a => a == PowerAction.RestoreSleep));

        // A session a crash, a restart or an offline quit left open gets its closing message,
        // once it is known whether sleep really came back.
        telegram.OnCommand = command => HandleCommand(command);
        telegram.Start(SystemReadings.BootTime, Read());

        // A closed PC waits for that closing message before it sleeps.
        if (actions.Contains(PowerAction.SleepNow))
        {
            if (telegram.HasPendingMessages)
            {
                sleepHeldUntil = DateTime.UtcNow + sleepHoldLimit;
                ReleaseHeldSleepAfterDrain();
            }
            else
            {
                Perform([PowerAction.SleepNow]);
            }
        }

        tray.Visible = true;
        timer.Start();
        CheckState();
    }

    /// <summary>Quit from the menu or asked to stop: give sleep back and let the closing message leave first.</summary>
    public async void Quit(CloseReason reason)
    {
        if (isTerminating)
        {
            return;
        }
        BeginQuitting(reason);
        if (telegram.HasPendingMessages)
        {
            await telegram.Drain(TimeSpan.FromSeconds(5));
        }
        Exit();
    }

    /// <summary>Logout, shutdown or restart. Windows ends the process soon after this returns, so it waits here.</summary>
    private void SessionEnding(bool logoff)
    {
        if (isTerminating)
        {
            return;
        }
        window.BlockShutdown("Gwendoline is sending its closing message");
        BeginQuitting(logoff ? CloseReason.Logout : CloseReason.Shutdown);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        while (telegram.HasPendingMessages && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
        Exit();
    }

    private void BeginQuitting(CloseReason reason)
    {
        isTerminating = true;
        timer.Stop();
        var state = Read();
        var restored = !state.SleepDisabled || LidSleep.Restore();
        SystemReadings.HoldAwake(false);
        if (!restored)
        {
            // Still on: say so, and keep the session on disk for the next launch to close with this reason.
            telegram.KeepOpenAfterQuit(reason);
            telegram.Notify(TelegramText.QuitStillOn, offButton: false);
        }
        else
        {
            telegram.Close(reason, state);
        }
    }

    private void Exit()
    {
        tray.Visible = false;
        SystemEvents.UserPreferenceChanged -= ThemeChanged;
        SystemEvents.DisplaySettingsChanged -= ThemeChanged;
        window.Dispose();
        ExitThread();
    }

    // Checks

    private void CheckState()
    {
        if (isTerminating)
        {
            return;
        }
        closedThisCheck = false;
        var policy = Policy;
        safety.Policy = policy;

        // Changed in Windows settings while on: the user's choice wins, Gwendoline turns off.
        if (LidSleep.Reconcile() is Guid changed)
        {
            var before = Read();
            if (LidSleep.Restore(keep: changed))
            {
                closedThisCheck = true;
                telegram.Close(CloseReason.Elsewhere, before with { SleepDisabled = false });
            }
        }

        var reading = Read();
        foreach (var notice in notifier.Check(reading))
        {
            telegram.Notify(TelegramText.On(reading), offButton: notice is Notice.BatteryLow);
        }

        var actions = safety.Check(reading);
        if (actions.Contains(PowerAction.RestoreSleep) && policy.DangerOf(reading) is Danger danger)
        {
            // Gwendoline gives sleep back on its own: battery floor.
            GiveSleepBack(actions, CloseReasons.From(danger), reading);
        }
        else
        {
            Perform(actions);
        }

        var state = actions.Count == 0 ? reading : Read();
        if (!state.SleepDisabled)
        {
            warnedCouldNotTurnOff = false;
        }
        if (!closedThisCheck)
        {
            telegram.Sync(state);
        }
        Render(state);
    }

    /// <summary>
    /// Turns Gwendoline off while it runs: icon or menu, Telegram, battery floor.
    /// Sleep comes back right away; a closed PC waits for the closing message before it sleeps.
    /// </summary>
    private void GiveSleepBack(IReadOnlyList<PowerAction> actions, CloseReason reason, PowerState state)
    {
        var sleeping = actions.Contains(PowerAction.SleepNow);
        if (sleeping && telegram.IsLinked)
        {
            sleepHeldUntil = DateTime.UtcNow + sleepHoldLimit;
        }
        Perform(actions);

        if (!changeAllowed)
        {
            // Windows refused: Gwendoline is still on, say so instead of closing the session.
            // Every /off gets its answer; the automatic tries warn once.
            sleepHeldUntil = null;
            if (reason == CloseReason.Telegram)
            {
                telegram.Reply(TelegramText.CouldNotTurnOff);
            }
            else if (!warnedCouldNotTurnOff)
            {
                warnedCouldNotTurnOff = true;
                telegram.Notify(TelegramText.CouldNotTurnOff, offButton: false);
            }
            return;
        }
        warnedCouldNotTurnOff = false;
        closedThisCheck = true;
        telegram.Close(reason, state);
        if (sleeping && telegram.IsLinked)
        {
            ReleaseHeldSleepAfterDrain();
        }
    }

    private async void ReleaseHeldSleepAfterDrain()
    {
        await telegram.Drain(sleepHoldLimit);
        // The closing message went out (or gave up): a closed PC can sleep now if it still has to.
        // Decided on a fresh reading, so a PC opened in the meantime stays awake.
        if (sleepHeldUntil == null || isTerminating)
        {
            return;
        }
        sleepHeldUntil = null;
        Perform(safety.Check(Read()));
    }

    private void Perform(IEnumerable<PowerAction> actions)
    {
        foreach (var action in actions)
        {
            if (isTerminating)
            {
                return;
            }
            switch (action)
            {
                case PowerAction.RestoreSleep:
                    changeAllowed = LidSleep.Restore();
                    if (!changeAllowed)
                    {
                        return;
                    }
                    break;
                case PowerAction.SleepNow:
                    if (sleepHeldUntil is DateTime until && DateTime.UtcNow < until)
                    {
                        continue;
                    }
                    var fresh = Read();
                    if (fresh.LidClosed && !fresh.ExternalDisplayConnected && !fresh.SleepDisabled)
                    {
                        SystemReadings.SleepNow();
                    }
                    break;
            }
        }
    }

    private void Render(PowerState state)
    {
        tray.Icon = state.SleepDisabled ? icons.On : icons.Off;
        tray.Text = StatusText.Tooltip(state);
        SystemReadings.HoldAwake(state.SleepDisabled);
    }

    private void ThemeChanged(object? sender, EventArgs e)
    {
        if (isTerminating)
        {
            return;
        }
        icons.Render();
        Render(Read());
    }

    // Claude Code

    /// <summary>
    /// A Claude Code hook call. Only while Gwendoline is on, when you are away: decided when the hook
    /// runs, so turning Gwendoline on during a long prompt is enough to hear when it ends. Off,
    /// Claude Code stays quiet on Telegram and asks in the terminal.
    /// </summary>
    public async Task<bool?> HandleClaude(ClaudeBridge.Request request)
    {
        if (isTerminating || !Read().SleepDisabled)
        {
            return null;
        }
        switch (request.Event)
        {
            case "done":
                telegram.Notify(ClaudeText.Done(request.Project, Random.Shared), offButton: false);
                return null;
            case "permission":
                return await telegram.AskPermission(ClaudeText.Ask(request.Tool ?? "?", request.Detail, request.Project, Random.Shared));
            default:
                return null;
        }
    }

    // Telegram commands

    private void HandleCommand(TelegramBot.Command command)
    {
        if (isTerminating)
        {
            return;
        }
        var state = Read();
        switch (command)
        {
            case TelegramBot.Command.Status:
                telegram.Reply(TelegramText.Status(state));
                break;
            case TelegramBot.Command.Off:
                if (!state.SleepDisabled)
                {
                    telegram.Reply(TelegramText.AlreadyOff);
                    return;
                }
                GiveSleepBack(safety.AllowSleep(state), CloseReason.Telegram, state);
                Render(Read());
                break;
        }
    }

    // Clicks

    private void Toggle()
    {
        if (isTerminating)
        {
            return;
        }
        var state = Read();
        var policy = Policy;

        if (state.SleepDisabled)
        {
            GiveSleepBack(safety.AllowSleep(state), CloseReason.Pc, state);
        }
        else
        {
            if (policy.DangerOf(state) is Danger danger)
            {
                var (title, message) = StatusText.Refusal(danger, policy);
                Alert(title, message);
                return;
            }
            changeAllowed = LidSleep.Enable();
            if (changeAllowed)
            {
                telegram.Sync(Read());
            }
        }
        if (!changeAllowed && !isTerminating)
        {
            Alert("Gwendoline could not change sleep",
                "Windows refused to change what closing the lid does. If this persists, run install.ps1 again.");
        }
        Render(Read());
    }

    private void BuildMenu(ContextMenuStrip menu, System.ComponentModel.CancelEventArgs e)
    {
        if (isTerminating)
        {
            e.Cancel = true;
            return;
        }
        var state = Read();
        Render(state);
        menu.Items.Clear();
        menu.Items.Add(Info(StatusText.Headline(state)));
        if (StatusText.Battery(state, Policy) is string battery)
        {
            menu.Items.Add(Info(battery));
        }
        if (!changeAllowed)
        {
            menu.Items.Add(Info("Not allowed yet, run install.ps1"));
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item(StatusText.ToggleTitle(state), Toggle));
        menu.Items.Add(new ToolStripSeparator());
        if (telegram.Problem is string problem)
        {
            menu.Items.Add(Info(problem));
        }
        if (telegram.IsLinked && telegram.BotUsername is string bot)
        {
            if (telegram.Problem == null)
            {
                menu.Items.Add(Info($"Telegram: @{bot}"));
            }
            menu.Items.Add(Item("Disconnect Telegram", DisconnectTelegram));
        }
        else if (telegram.IsWaitingForStart)
        {
            menu.Items.Add(Info("Telegram: tap Start in the bot chat"));
            menu.Items.Add(Item("Open the Bot Chat…", OpenBotChat));
            menu.Items.Add(Item("Disconnect Telegram", DisconnectTelegram));
        }
        else
        {
            menu.Items.Add(Item("Connect Telegram…", ConnectTelegram));
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Quit Gwendoline", () => Quit(CloseReason.Quit)));
        e.Cancel = false;
    }

    private static ToolStripMenuItem Info(string title) => new(title) { Enabled = false };

    private static ToolStripMenuItem Item(string title, Action action) => new(title, null, (_, _) => action());

    // Telegram setup

    private async void ConnectTelegram()
    {
        if (isTerminating || TelegramDialogs.AskToken() is not string token)
        {
            return;
        }
        try
        {
            await telegram.Connect(token);
            OpenBotChat();
        }
        catch (TelegramFailure failure)
        {
            Alert("Telegram is not connected", failure.Message);
        }
    }

    private void OpenBotChat()
    {
        if (isTerminating || telegram.StartLinks is not { } links || telegram.BotUsername is not string bot)
        {
            return;
        }
        TelegramDialogs.ShowStart(bot, links);
    }

    private async void DisconnectTelegram()
    {
        if (isTerminating)
        {
            return;
        }
        // Settle the session first, so the last message says the truth about Gwendoline.
        telegram.Sync(Read());
        if (await telegram.Disconnect() is string problem)
        {
            Alert("Telegram was not told", problem);
        }
    }

    private static void Alert(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            tray.Dispose();
            icons.Dispose();
        }
        base.Dispose(disposing);
    }
}
