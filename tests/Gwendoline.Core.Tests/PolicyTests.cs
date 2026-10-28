using Gwendoline.Core;

namespace Gwendoline.Core.Tests;

public class BatteryFloorTests
{
    private readonly Policy policy = new(batteryFloor: 20);

    [Fact]
    public void TriggersAtTheFloorOnBattery() =>
        Assert.Equal(Danger.LowBattery, policy.DangerOf(new PowerState { SleepDisabled = true, OnBattery = true, BatteryPercent = 20 }));

    [Fact]
    public void StaysQuietAboveTheFloor() =>
        Assert.Null(policy.DangerOf(new PowerState { SleepDisabled = true, OnBattery = true, BatteryPercent = 21 }));

    [Fact]
    public void IgnoresTheFloorWhileCharging() =>
        Assert.Null(policy.DangerOf(new PowerState { SleepDisabled = true, BatteryCharging = true, BatteryPercent = 5 }));

    [Fact]
    public void TriggersOnAnAdapterTooWeakToCharge() =>
        Assert.Equal(Danger.LowBattery, policy.DangerOf(new PowerState { SleepDisabled = true, BatteryPercent = 15 }));

    [Fact]
    public void ZeroDisablesTheFloor() =>
        Assert.Null(new Policy(0).DangerOf(new PowerState { SleepDisabled = true, OnBattery = true, BatteryPercent = 3 }));

    [Fact]
    public void ClampsOutOfRangeFloors()
    {
        Assert.Equal(0, new Policy(-5).BatteryFloor);
        Assert.Equal(100, new Policy(250).BatteryFloor);
    }

    [Fact]
    public void TheDefaultFloorIsALastResort() => Assert.Equal(5, new Policy().BatteryFloor);

    [Fact]
    public void DesktopPcsHaveNoFloor() =>
        Assert.Null(policy.DangerOf(new PowerState { SleepDisabled = true, BatteryPercent = null }));
}

public class GivingSleepBackTests
{
    private readonly PowerState lowOnDesk = new() { SleepDisabled = true, OnBattery = true, BatteryPercent = 4 };

    [Fact]
    public void RestoresSleepWithTheLidOpen()
    {
        var safety = new Safety();
        Assert.Equal([PowerAction.RestoreSleep], safety.Check(lowOnDesk));
        Assert.False(safety.SleepPending);
    }

    [Fact]
    public void AlsoSleepsNowWhenTheLidIsClosed()
    {
        var safety = new Safety();
        Assert.Equal([PowerAction.RestoreSleep, PowerAction.SleepNow], safety.Check(lowOnDesk with { LidClosed = true }));
        Assert.True(safety.SleepPending);
    }

    [Fact]
    public void NeverForcesSleepOnADockedPc()
    {
        var safety = new Safety();
        var docked = lowOnDesk with { LidClosed = true, ExternalDisplayConnected = true };
        Assert.Equal([PowerAction.RestoreSleep], safety.Check(docked));
    }

    [Fact]
    public void KeepsAskingUntilThePcSleeps()
    {
        var safety = new Safety();
        var closed = lowOnDesk with { LidClosed = true };
        safety.Check(closed);
        // Sleep is back but the PC is still awake on the next check.
        closed = closed with { SleepDisabled = false };
        Assert.Equal([PowerAction.SleepNow], safety.Check(closed));
        Assert.Equal([PowerAction.SleepNow], safety.Check(closed));
    }

    [Fact]
    public void StopsAskingOnceThePcHasSlept()
    {
        var safety = new Safety();
        var closed = lowOnDesk with { LidClosed = true };
        safety.Check(closed);
        safety.DidWake();
        Assert.Empty(safety.Check(closed with { SleepDisabled = false }));
    }

    [Fact]
    public void StopsAskingWhenTheLidOpens()
    {
        var safety = new Safety();
        safety.Check(lowOnDesk with { LidClosed = true });
        Assert.Empty(safety.Check(lowOnDesk with { SleepDisabled = false, LidClosed = false }));
        Assert.False(safety.SleepPending);
    }

    [Fact]
    public void DoesNothingWhileSleepIsAllowed()
    {
        var safety = new Safety();
        Assert.Empty(safety.Check(new PowerState { OnBattery = true, BatteryPercent = 2, LidClosed = true }));
    }
}

public class LaunchTests
{
    [Fact]
    public void AlwaysRestoresSleep()
    {
        var safety = new Safety();
        Assert.Equal([PowerAction.RestoreSleep], safety.Launch(new PowerState()));
        Assert.Equal([PowerAction.RestoreSleep], safety.Launch(new PowerState { SleepDisabled = true }));
    }

    [Fact]
    public void PutsAClosedPcToSleep()
    {
        var safety = new Safety();
        Assert.Equal([PowerAction.RestoreSleep, PowerAction.SleepNow], safety.Launch(new PowerState { SleepDisabled = true, LidClosed = true }));
        Assert.True(safety.SleepPending);
    }
}

public class ManualOffTests
{
    [Fact]
    public void AClosedPcGoesToSleep()
    {
        var safety = new Safety();
        Assert.Equal([PowerAction.RestoreSleep, PowerAction.SleepNow], safety.AllowSleep(new PowerState { SleepDisabled = true, LidClosed = true }));
        Assert.True(safety.SleepPending);
    }

    [Fact]
    public void AnOpenPcJustSleepsNormally()
    {
        var safety = new Safety();
        Assert.Equal([PowerAction.RestoreSleep], safety.AllowSleep(new PowerState { SleepDisabled = true }));
        Assert.False(safety.SleepPending);
    }
}
