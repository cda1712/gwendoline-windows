namespace Gwendoline.Core;

/// <summary>
/// The menu and tooltip texts that change with the state, and the alert when the battery stops Gwendoline from turning on.
/// </summary>
public static class StatusText
{
    public static string Headline(PowerState state)
    {
        return state.SleepDisabled ? "Awake, even with the lid closed" : "Sleeping normally";
    }

    public static string? Battery(PowerState state, Policy policy)
    {
        if (state.BatteryPercent is not int percent)
        {
            return null;
        }
        if (!state.OnBattery)
        {
            return state.BatteryCharging ? $"Battery {percent}%, charging" : $"Battery {percent}%, plugged in";
        }
        if (policy.BatteryFloor <= 0)
        {
            return $"Battery {percent}%, no battery floor";
        }
        return $"Battery {percent}%, sleeps again at {policy.BatteryFloor}%";
    }

    public static string ToggleTitle(PowerState state)
    {
        return state.SleepDisabled ? "Allow Sleep" : "Stay Awake";
    }

    /// <summary>The tray icon tooltip. Windows cuts it at 127 characters.</summary>
    public static string Tooltip(PowerState state)
    {
        var next = state.SleepDisabled ? "allow sleep again" : "stay awake with the lid closed";
        return $"Gwendoline: {Headline(state).ToLowerInvariant()}. Click to {next}, right-click for more.";
    }

    /// <summary>Title and message of the alert shown when staying awake is refused.</summary>
    public static (string Title, string Message) Refusal(Danger danger, Policy policy)
    {
        return danger switch
        {
            Danger.LowBattery => (
                "Battery too low",
                $"Gwendoline lets the PC sleep at {policy.BatteryFloor}% when the battery is not charging. "
                    + "Plug in a power adapter first."),
            _ => throw new ArgumentOutOfRangeException(nameof(danger)),
        };
    }
}
