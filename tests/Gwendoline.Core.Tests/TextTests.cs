using Gwendoline.Core;
using static Gwendoline.Core.Tests.Readings;

namespace Gwendoline.Core.Tests;

public class StatusTextTests
{
    private readonly Policy policy = new(batteryFloor: 20);

    [Fact]
    public void DescribesBothStates()
    {
        Assert.Equal("Awake, even with the lid closed", StatusText.Headline(new PowerState { SleepDisabled = true }));
        Assert.Equal("Sleeping normally", StatusText.Headline(new PowerState()));
    }

    [Fact]
    public void DescribesTheBattery()
    {
        Assert.Null(StatusText.Battery(new PowerState { SleepDisabled = true }, policy));
        Assert.Equal("Battery 72%, sleeps again at 20%", StatusText.Battery(OnBattery(72), policy));
        Assert.Equal("Battery 72%, charging", StatusText.Battery(Charging(72), policy));
        Assert.Equal("Battery 100%, plugged in", StatusText.Battery(Charged(100), policy));
    }

    [Fact]
    public void ExplainsRefusals() =>
        Assert.Contains("20%", StatusText.Refusal(Danger.LowBattery, policy).Message);

    [Fact]
    public void TooltipFitsTheTray()
    {
        Assert.True(StatusText.Tooltip(new PowerState()).Length <= 127);
        Assert.True(StatusText.Tooltip(new PowerState { SleepDisabled = true }).Length <= 127);
    }
}

public class TelegramTextTests
{
    [Fact]
    public void OnWithTimeLeft() =>
        Assert.Equal("Gwendoline is on\nBattery 94%, 20 h left", TelegramText.On(OnBattery(94) with { MinutesToEmpty = 1_200 }));

    [Fact]
    public void BatteryLines()
    {
        Assert.Equal("Battery 41%, on battery", TelegramText.Battery(OnBattery(41)));
        Assert.Equal("Battery 50%, charging", TelegramText.Battery(Charging(50)));
        Assert.Equal("Battery 100%, charged", TelegramText.Battery(Charged(100)));
        Assert.Equal("Battery 60%, plugged in, not charging", TelegramText.Battery(WeakAdapter(60)));
        Assert.Equal("No battery", TelegramText.Battery(new PowerState()));
    }

    [Fact]
    public void TimeLeftOnlyOnBattery() =>
        Assert.Equal("Battery 80%, charged", TelegramText.Battery(Charged(80) with { MinutesToEmpty = 200 }));

    [Fact]
    public void StatusSaysOnOrOff()
    {
        Assert.StartsWith("Gwendoline is on\n", TelegramText.Status(OnBattery(41)));
        Assert.Equal("Gwendoline is off\nBattery 41%, on battery", TelegramText.Status(OnBattery(41, awake: false)));
    }

    [Theory]
    [InlineData(CloseReason.Pc)]
    [InlineData(CloseReason.Telegram)]
    [InlineData(CloseReason.Quit)]
    [InlineData(CloseReason.Stopped)]
    [InlineData(CloseReason.Elsewhere)]
    public void ClosingIsBareWhenTurnedOffByHand(CloseReason reason) =>
        Assert.Equal("Gwendoline is off\nBattery 90%, on battery", TelegramText.Closed(reason, OnBattery(90)));

    [Fact]
    public void ClosingSaysWhyOtherwise()
    {
        Assert.Equal("Gwendoline is off (battery low)\nBattery 5%, on battery", TelegramText.Closed(CloseReason.LowBattery, OnBattery(5)));
        Assert.StartsWith("Gwendoline is off (PC shut down)\n", TelegramText.Closed(CloseReason.Shutdown, OnBattery(50)));
        Assert.StartsWith("Gwendoline is off (Gwendoline crashed)\n", TelegramText.Closed(CloseReason.Crash, OnBattery(50)));
        Assert.StartsWith("Gwendoline is off (PC restarted)\n", TelegramText.Closed(CloseReason.UnexpectedRestart, OnBattery(50)));
        Assert.StartsWith("Gwendoline is off (logged out)\n", TelegramText.Closed(CloseReason.Logout, OnBattery(50)));
    }

    [Fact]
    public void EveryClosingStartsTheSameWay()
    {
        foreach (var reason in Enum.GetValues<CloseReason>())
        {
            Assert.StartsWith("Gwendoline is off", TelegramText.Closed(reason, OnBattery(50)));
        }
    }

    [Fact]
    public void Durations()
    {
        Assert.Equal("under 1 min", TelegramText.Duration(0));
        Assert.Equal("45 min", TelegramText.Duration(45));
        Assert.Equal("1 h", TelegramText.Duration(60));
        Assert.Equal("2 h 15 min", TelegramText.Duration(135));
    }
}
