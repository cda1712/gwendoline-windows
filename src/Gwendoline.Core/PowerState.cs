namespace Gwendoline.Core;

/// <summary>Everything Gwendoline reads from the system on each check.</summary>
public sealed record PowerState
{
    /// <summary>Gwendoline holds the PC awake: every power plan's lid action is Do nothing and idle sleep is blocked.</summary>
    public bool SleepDisabled { get; init; }
    /// <summary>True when the PC runs on its battery rather than a power adapter.</summary>
    public bool OnBattery { get; init; }
    /// <summary>True while the battery charges. False on a weak adapter that cannot keep up.</summary>
    public bool BatteryCharging { get; init; }
    /// <summary>Battery charge from 0 to 100, null on PCs without a battery.</summary>
    public int? BatteryPercent { get; init; }
    /// <summary>Minutes of battery left while unplugged, null when Windows has no estimate yet.</summary>
    public int? MinutesToEmpty { get; init; }
    /// <summary>Plugged in and charging has ended: the battery is full, or held at a charge limit.</summary>
    public bool ChargeComplete { get; init; }
    /// <summary>The laptop lid is closed.</summary>
    public bool LidClosed { get; init; }
    /// <summary>A screen other than the built-in one is connected.</summary>
    public bool ExternalDisplayConnected { get; init; }

    /// <summary>Closed lid and no monitor: nobody is using this PC, it may well be in a bag.</summary>
    internal bool ClosedWithoutExternalDisplay => LidClosed && !ExternalDisplayConnected;

    /// <summary>The battery loses charge: on battery, or plugged into an adapter that does not charge it.</summary>
    internal bool Draining => OnBattery || !BatteryCharging;
}
