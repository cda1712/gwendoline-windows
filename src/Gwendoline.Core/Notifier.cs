namespace Gwendoline.Core;

/// <summary>Something worth a Telegram message while Gwendoline is on.</summary>
public abstract record Notice
{
    /// <summary>The battery went down past one of the reminder levels.</summary>
    public sealed record BatteryLow(int Percent) : Notice;

    /// <summary>Plugged in and charging has ended: full, or held at a charge limit.</summary>
    public sealed record ChargingDone(int Percent) : Notice;
}

/// <summary>
/// Decides when to send a reminder, from one reading to the next.
/// Opening and closing messages are not here: they follow Gwendoline turning on and off.
/// </summary>
public sealed class Notifier
{
    /// <summary>Battery levels that get a reminder, once each per discharge.</summary>
    internal static readonly int[] ReminderLevels = [30, 20, 10];
    /// <summary>How far the battery must charge back above a level before its reminder can fire again.</summary>
    internal const int RearmMargin = 5;
    /// <summary>
    /// How far the charge must move from the last "charging done" before another one,
    /// so a full battery drifting between 95% and 100% stays quiet while 80% then 100% does not.
    /// </summary>
    internal const int ChargeMargin = 10;

    private HashSet<int> reminded = [];
    private int? lastChargedPercent;
    /// <summary>A charge was watched on the adapter since the last battery reading: replugging a full battery is not one.</summary>
    private bool sawCharging;
    private PowerState? previous;

    public IReadOnlyList<Notice> Check(PowerState current)
    {
        try
        {
            return Evaluate(current);
        }
        finally
        {
            previous = current;
        }
    }

    private List<Notice> Evaluate(PowerState current)
    {
        if (!current.SleepDisabled)
        {
            reminded = [];
            lastChargedPercent = null;
            sawCharging = false;
            return [];
        }
        if (current.BatteryPercent is not int percent)
        {
            return [];
        }
        var wasOn = previous?.SleepDisabled ?? false;
        var notices = new List<Notice>();

        if (current.Draining)
        {
            var crossed = ReminderLevels.Where(level => percent <= level && !reminded.Contains(level)).ToList();
            reminded.UnionWith(crossed);
            // A level counts when the battery went past it while watched. When the drain just started
            // (Gwendoline turned on, cable out, charging stopped), levels at or above the previous
            // reading were already behind: they are the starting point, without a message.
            var watched = wasOn && (previous?.Draining ?? false);
            var startingPoint = watched ? int.MaxValue : (previous?.BatteryPercent ?? percent);
            if (crossed.Any(level => level < startingPoint))
            {
                notices.Add(new Notice.BatteryLow(percent));
            }
        }
        else
        {
            reminded = reminded.Where(level => percent < level + RearmMargin).ToHashSet();
        }

        if (current.OnBattery)
        {
            lastChargedPercent = null;
            sawCharging = false;
        }
        else if (current.BatteryCharging)
        {
            sawCharging = true;
        }
        if (lastChargedPercent is int last && Math.Abs(percent - last) >= ChargeMargin)
        {
            lastChargedPercent = null;
        }
        if (!current.OnBattery && !current.BatteryCharging && current.ChargeComplete && lastChargedPercent == null)
        {
            lastChargedPercent = percent;
            // Already charged when Gwendoline turned on, or plugged in already full: nothing was watched charging.
            if (wasOn && sawCharging)
            {
                notices.Add(new Notice.ChargingDone(percent));
            }
        }
        return notices;
    }
}
