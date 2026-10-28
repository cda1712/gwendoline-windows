using Gwendoline.Core;
using static Gwendoline.Core.Tests.Readings;

namespace Gwendoline.Core.Tests;

public class BatteryReminderTests
{
    /// <summary>Runs readings through a notifier and returns everything it produced.</summary>
    internal static List<Notice> Run(params PowerState[] readings)
    {
        var notifier = new Notifier();
        return readings.SelectMany(notifier.Check).ToList();
    }

    private static Notice Low(int percent) => new Notice.BatteryLow(percent);

    [Fact]
    public void RemindsAt30And20And10() =>
        Assert.Equal([Low(30), Low(20), Low(10)],
            Run(OnBattery(35), OnBattery(30), OnBattery(25), OnBattery(20), OnBattery(11), OnBattery(10)));

    [Fact]
    public void RemindsOnlyOncePerLevel() =>
        Assert.Equal([Low(30)], Run(OnBattery(31), OnBattery(30), OnBattery(30), OnBattery(29)));

    [Fact]
    public void StaysQuietAboutTheLevelFoundWhenTurnedOn() =>
        Assert.Equal([Low(20)], Run(OnBattery(25, awake: false), OnBattery(25), OnBattery(24), OnBattery(20)));

    [Fact]
    public void CatchesALevelCrossedRightAfterTurningOn() =>
        Assert.Equal([Low(30)], Run(OnBattery(31, awake: false), OnBattery(30)));

    [Fact]
    public void StaysQuietWhenTheCableComesOut() =>
        Assert.Equal([Low(20)], Run(Charging(26), OnBattery(26), OnBattery(21), OnBattery(20)));

    [Fact]
    public void CatchesALevelCrossedWhenChargingStops() =>
        Assert.Equal([Low(20)], Run(Charging(21), WeakAdapter(20)));

    [Fact]
    public void RemindsAgainAfterChargingWellAboveTheLevel() =>
        Assert.Equal([Low(30), Low(30)],
            Run(OnBattery(31), OnBattery(30), Charging(30), Charging(45), OnBattery(45), OnBattery(31), OnBattery(30)));

    [Fact]
    public void AFlappingAdapterDoesNotRepeatTheReminder() =>
        Assert.Equal([Low(30)],
            Run(OnBattery(31), OnBattery(30), Charging(31), WeakAdapter(30), Charging(31), WeakAdapter(30)));

    [Fact]
    public void CountsAWeakAdapterAsDraining() =>
        Assert.Equal([Low(20)], Run(WeakAdapter(21), WeakAdapter(20)));

    [Fact]
    public void StaysQuietWhileSleepIsAllowed() =>
        Assert.Empty(Run(OnBattery(31, awake: false), OnBattery(30, awake: false), OnBattery(10, awake: false)));

    [Fact]
    public void StartsOverEachTimeGwendolineTurnsOn() =>
        Assert.Equal([Low(30), Low(20)],
            Run(OnBattery(31), OnBattery(30), OnBattery(29, awake: false), OnBattery(29), OnBattery(20)));

    [Fact]
    public void DesktopPcsGetNoReminder() =>
        Assert.Empty(Run(new PowerState { SleepDisabled = true }, new PowerState { SleepDisabled = true }));
}

public class ChargingDoneTests
{
    private static List<Notice> Run(params PowerState[] readings) => BatteryReminderTests.Run(readings);

    private static Notice Done(int percent) => new Notice.ChargingDone(percent);

    [Fact]
    public void SaysWhenTheBatteryIsFull() =>
        Assert.Equal([Done(100)], Run(Charging(98), Charging(100), Charged(100)));

    [Fact]
    public void IgnoresAPauseOnAWeakAdapter() =>
        Assert.Empty(Run(Charging(60), WeakAdapter(61), Charging(62), WeakAdapter(62)));

    [Fact]
    public void IsNotFooledByUnplugging() =>
        Assert.Empty(Run(Charging(100), OnBattery(100)));

    [Fact]
    public void StaysQuietWhenAlreadyChargedAtTurnOn() =>
        Assert.Empty(Run(Charged(100, awake: false), Charged(100), Charged(100)));

    [Fact]
    public void DoesNotRepeatWhileAFullBatteryDrifts() =>
        Assert.Equal([Done(100)], Run(Charging(99), Charged(100), Charging(95), Charged(100)));

    [Fact]
    public void ReportsTheHoldAt80ThenTheFullCharge() =>
        Assert.Equal([Done(80), Done(100)], Run(Charging(79), Charged(80), Charging(85), Charging(95), Charged(100)));

    [Fact]
    public void StaysQuietWhenAFullBatteryIsPluggedBackIn()
    {
        Assert.Empty(Run(Charged(100), OnBattery(100), Charged(100)));
        Assert.Empty(Run(OnBattery(97), Charged(97)));
    }

    [Fact]
    public void StaysQuietWhenRepluggingDuringThe80Hold() =>
        Assert.Equal([Done(80)], Run(Charging(79), Charged(80), OnBattery(80), Charged(80)));

    [Fact]
    public void ReportsAgainAfterADrainAndAFullRecharge() =>
        Assert.Equal([Done(100), Done(100)],
            Run(Charging(99), Charged(100), OnBattery(60), Charging(70), Charging(99), Charged(100)));
}
