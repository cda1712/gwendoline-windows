using Gwendoline.Core;

namespace Gwendoline;

/// <summary>Reads the battery, the lid and the screens, and puts the PC to sleep. No administrator rights needed.</summary>
internal static class SystemReadings
{
    public static PowerState Current(PowerWindow window)
    {
        var (onBattery, charging, percent, minutesToEmpty) = Battery();
        return new PowerState
        {
            SleepDisabled = LidSleep.IsOn,
            OnBattery = onBattery,
            BatteryCharging = charging,
            BatteryPercent = percent,
            MinutesToEmpty = minutesToEmpty,
            // Windows does not say why a plugged-in battery stopped charging: full, or held at the
            // laptop's charge limit. Either way charging has ended.
            ChargeComplete = percent != null && !onBattery && !charging,
            LidClosed = window.LidClosed,
            ExternalDisplayConnected = ExternalDisplayConnected(),
        };
    }

    private static (bool OnBattery, bool Charging, int? Percent, int? MinutesToEmpty) Battery()
    {
        if (!Native.GetSystemPowerStatus(out var status))
        {
            return (false, false, null, null);
        }
        var onBattery = status.ACLineStatus == 0;
        var hasBattery = status.BatteryFlag != 128 && status.BatteryFlag != 255 && status.BatteryLifePercent <= 100;
        if (!hasBattery)
        {
            return (onBattery, false, null, null);
        }
        var charging = (status.BatteryFlag & 8) != 0;
        int? minutes = onBattery && status.BatteryLifeTime > 0 ? status.BatteryLifeTime / 60 : null;
        return (onBattery, charging, status.BatteryLifePercent, minutes);
    }

    /// <summary>An active screen that is not built in. A closed lid turns the built-in one off when another is there.</summary>
    private static bool ExternalDisplayConnected()
    {
        if (Native.GetDisplayConfigBufferSizes(Native.QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0)
        {
            return false;
        }
        var paths = new Native.DisplayConfigPathInfo[pathCount];
        var modes = new Native.DisplayConfigModeInfo[modeCount];
        if (Native.QueryDisplayConfig(Native.QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
        {
            return false;
        }
        return paths.Take((int)pathCount).Any(path => path.OutputTechnology is not
            (Native.OutputInternal or Native.OutputDisplayPortEmbedded or Native.OutputUdiEmbedded));
    }

    /// <summary>Blocks idle sleep while Gwendoline is on. Windows drops the request if Gwendoline exits.</summary>
    public static void HoldAwake(bool awake)
    {
        Native.SetThreadExecutionState(awake ? Native.EsContinuous | Native.EsSystemRequired : Native.EsContinuous);
    }

    /// <summary>Sleep, as from the Start menu. On a Modern Standby PC this is Modern Standby.</summary>
    public static void SleepNow()
    {
        Native.SetSuspendState(hibernate: false, force: false, wakeupEventsDisabled: false);
    }

    /// <summary>When Windows last started, to tell a crash of Gwendoline from a restart of the PC.</summary>
    public static DateTimeOffset BootTime => DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
}
