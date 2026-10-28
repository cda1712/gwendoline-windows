namespace Gwendoline.Core;

/// <summary>What the app must do after a check.</summary>
public enum PowerAction
{
    /// <summary>Give the lid action back to every power plan and stop blocking idle sleep.</summary>
    RestoreSleep,
    /// <summary>Put the PC to sleep right away.</summary>
    SleepNow,
}

/// <summary>Why staying awake is not allowed right now.</summary>
public enum Danger
{
    LowBattery,
}

/// <summary>The safety thresholds, free of system calls so they can be tested.</summary>
public sealed record Policy
{
    /// <summary>A last resort: the Telegram reminders at 30, 20 and 10% let you turn it off yourself before.</summary>
    public const int DefaultBatteryFloor = 5;

    /// <summary>
    /// Battery percentage at or below which Gwendoline gives sleep back while the battery drains.
    /// 0 disables the floor.
    /// </summary>
    public int BatteryFloor { get; }

    public Policy(int batteryFloor = DefaultBatteryFloor)
    {
        BatteryFloor = Math.Clamp(batteryFloor, 0, 100);
    }

    /// <summary>
    /// Returns why the PC must be allowed to sleep, or null if staying awake is fine.
    /// Windows keeps its own critical battery action and thermal shutdown while Gwendoline is on,
    /// the floor is there so a closed PC in a bag sleeps well before that.
    /// </summary>
    public Danger? DangerOf(PowerState state)
    {
        if (BatteryFloor > 0 && state.Draining && state.BatteryPercent is int percent && percent <= BatteryFloor)
        {
            return Danger.LowBattery;
        }
        return null;
    }
}

/// <summary>The safety rules over time. Remembers a sleep still owed.</summary>
public sealed class Safety
{
    public Policy Policy { get; set; } = new();

    /// <summary>
    /// The PC must sleep but has not yet. Windows does not put an already closed PC to sleep
    /// when the lid action comes back, so the request is repeated on every check until the PC
    /// sleeps or the lid opens.
    /// </summary>
    public bool SleepPending { get; private set; }

    /// <summary>At launch Gwendoline always gives sleep back, so a crash or a restart never leaves the PC stuck awake.</summary>
    public IReadOnlyList<PowerAction> Launch(PowerState state)
    {
        return state.SleepDisabled ? AllowSleep(state) : [PowerAction.RestoreSleep];
    }

    /// <summary>
    /// Gives sleep back, and puts a closed PC without a monitor to sleep right away.
    /// For a launch, a danger, or Gwendoline turned off by hand from the tray or Telegram.
    /// </summary>
    public IReadOnlyList<PowerAction> AllowSleep(PowerState state)
    {
        if (!state.ClosedWithoutExternalDisplay)
        {
            return [PowerAction.RestoreSleep];
        }
        SleepPending = true;
        return [PowerAction.RestoreSleep, PowerAction.SleepNow];
    }

    /// <summary>Decides what to do after a new reading.</summary>
    public IReadOnlyList<PowerAction> Check(PowerState current)
    {
        if (SleepPending)
        {
            if (!current.ClosedWithoutExternalDisplay)
            {
                SleepPending = false;
            }
            else if (!current.SleepDisabled)
            {
                return [PowerAction.SleepNow];
            }
            else
            {
                // Turned on again in the meantime, the rules below take over.
                SleepPending = false;
            }
        }

        if (!current.SleepDisabled)
        {
            return [];
        }
        return Policy.DangerOf(current) != null ? AllowSleep(current) : [];
    }

    /// <summary>The PC slept and woke up: any sleep owed has happened.</summary>
    public void DidWake()
    {
        SleepPending = false;
    }
}
