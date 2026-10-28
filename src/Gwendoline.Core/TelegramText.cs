namespace Gwendoline.Core;

/// <summary>Why Gwendoline turned off, told in the closing message.</summary>
public enum CloseReason
{
    /// <summary>The tray icon or its menu.</summary>
    Pc,
    /// <summary><c>/off</c> or the Turn Gwendoline off button.</summary>
    Telegram,
    LowBattery,
    /// <summary>Quit in the menu.</summary>
    Quit,
    /// <summary>Asked to stop, for example while reinstalling.</summary>
    Stopped,
    /// <summary>Shutdown or restart: Windows does not tell them apart.</summary>
    Shutdown,
    Logout,
    /// <summary>Gwendoline crashed, its supervisor started it again.</summary>
    Crash,
    /// <summary>The PC restarted without a clean shutdown: power loss, crash, forced restart.</summary>
    UnexpectedRestart,
    /// <summary>The lid action changed in Windows settings while Gwendoline was on.</summary>
    Elsewhere,
}

public static class CloseReasons
{
    /// <summary>Gwendoline gave sleep back on its own.</summary>
    public static CloseReason From(Danger danger)
    {
        return danger switch
        {
            Danger.LowBattery => CloseReason.LowBattery,
            _ => throw new ArgumentOutOfRangeException(nameof(danger)),
        };
    }
}

/// <summary>
/// Everything the Telegram bot says: whether Gwendoline is on or off, then the battery.
/// "Off" always means Gwendoline stopped keeping the PC awake, never that the PC shuts down.
/// </summary>
public static class TelegramText
{
    /// <summary>The bot menu in Telegram.</summary>
    public static readonly IReadOnlyList<(string Command, string Description)> Commands =
    [
        ("status", "Battery and state"),
        ("off", "Turn Gwendoline off, the PC sleeps normally again"),
    ];

    /// <summary>The answer to anything that is not a command.</summary>
    public static readonly string Help = string.Join("\n", Commands.Select(c => $"/{c.Command}  {c.Description}"));

    public static readonly string Connected = $"Gwendoline is connected.\n\n{Help}";

    public const string OffButton = "Turn Gwendoline off";
    public const string AlreadyOff = "Gwendoline is already off";
    public const string LateOff = "Gwendoline was turned on again after your /off. Send /off again to turn it off.";
    public const string StaleButton = "This button is from an earlier session.";
    public const string UseTheLink = "To connect, open the link Gwendoline shows on your PC.";
    public const string CouldNotTurnOff = "Gwendoline is still on, it could not turn off. Run install.ps1 again on the PC.";
    public const string QuitStillOn = "Gwendoline quit but is still on. Run install.ps1 again on the PC.";
    /// <summary>The last message when Telegram is disconnected while Gwendoline is still on. It must not say "off".</summary>
    public const string Disconnected = "Gwendoline is disconnected from this chat. It is still on.";

    /// <summary>The opening message, the reminders, the charging message and /status: the state, then the battery.</summary>
    public static string On(PowerState state)
    {
        return $"Gwendoline is on\n{Battery(state)}";
    }

    public static string Status(PowerState state)
    {
        return state.SleepDisabled ? On(state) : $"Gwendoline is off\n{Battery(state)}";
    }

    /// <summary>The closing message. A short reason only when it was not turned off by hand.</summary>
    public static string Closed(CloseReason reason, PowerState state)
    {
        var tag = reason switch
        {
            CloseReason.LowBattery => " (battery low)",
            CloseReason.Shutdown => " (PC shut down)",
            CloseReason.Logout => " (logged out)",
            CloseReason.Crash => " (Gwendoline crashed)",
            CloseReason.UnexpectedRestart => " (PC restarted)",
            _ => "",
        };
        return $"Gwendoline is off{tag}\n{Battery(state)}";
    }

    internal static string Battery(PowerState state)
    {
        if (state.BatteryPercent is not int percent)
        {
            return "No battery";
        }
        if (state.OnBattery)
        {
            return state.MinutesToEmpty is int minutes
                ? $"Battery {percent}%, {Duration(minutes)} left"
                : $"Battery {percent}%, on battery";
        }
        if (state.BatteryCharging)
        {
            return $"Battery {percent}%, charging";
        }
        return state.ChargeComplete ? $"Battery {percent}%, charged" : $"Battery {percent}%, plugged in, not charging";
    }

    internal static string Duration(int minutes)
    {
        if (minutes < 1)
        {
            return "under 1 min";
        }
        if (minutes < 60)
        {
            return $"{minutes} min";
        }
        int hours = minutes / 60, rest = minutes % 60;
        return rest == 0 ? $"{hours} h" : $"{hours} h {rest} min";
    }
}
