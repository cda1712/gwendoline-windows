using Gwendoline.Core;

namespace Gwendoline.Core.Tests;

/// <summary>Readings shared by the test classes.</summary>
internal static class Readings
{
    public static PowerState OnBattery(int percent, bool awake = true) =>
        new() { SleepDisabled = awake, OnBattery = true, BatteryPercent = percent };

    public static PowerState Charging(int percent, bool awake = true) =>
        new() { SleepDisabled = awake, BatteryCharging = true, BatteryPercent = percent };

    /// <summary>Plugged in, not charging, and charging has not ended: a weak adapter.</summary>
    public static PowerState WeakAdapter(int percent, bool awake = true) =>
        new() { SleepDisabled = awake, BatteryPercent = percent };

    /// <summary>Plugged in and charging has ended (full, or held at a charge limit).</summary>
    public static PowerState Charged(int percent, bool awake = true) =>
        new() { SleepDisabled = awake, BatteryPercent = percent, ChargeComplete = true };
}
