using System.Runtime.InteropServices;

namespace Gwendoline;

/// <summary>
/// A hidden window: Windows tells the lid, the wake from sleep and the end of the session to a window only.
/// </summary>
internal sealed class PowerWindow : NativeWindow, IDisposable
{
    private IntPtr lidNotification;

    /// <summary>Windows sends the lid state once right after registration, then on every change.</summary>
    public bool LidClosed { get; private set; }

    public event Action? Woke;
    /// <summary>The session ends; the argument tells a logout from a shutdown or restart.</summary>
    public event Action<bool>? SessionEnding;

    public PowerWindow()
    {
        CreateHandle(new CreateParams { Caption = "Gwendoline" });
        var lid = Native.LidSwitchStateChange;
        lidNotification = Native.RegisterPowerSettingNotification(Handle, ref lid, 0);
    }

    /// <summary>While set, Windows shows this reason on its shutdown screen and waits a little for Gwendoline.</summary>
    public void BlockShutdown(string reason)
    {
        Native.ShutdownBlockReasonCreate(Handle, reason);
    }

    protected override void WndProc(ref Message message)
    {
        switch (message.Msg)
        {
            case Native.WmPowerBroadcast when (int)message.WParam == Native.PbtPowerSettingChange:
                var setting = Marshal.PtrToStructure<Native.PowerBroadcastSetting>(message.LParam);
                if (setting.PowerSetting == Native.LidSwitchStateChange)
                {
                    LidClosed = setting.Data == 0;
                }
                message.Result = 1;
                return;
            case Native.WmPowerBroadcast when (int)message.WParam == Native.PbtApmResumeAutomatic:
                Woke?.Invoke();
                message.Result = 1;
                return;
            case Native.WmQueryEndSession:
                message.Result = 1;
                return;
            case Native.WmEndSession when message.WParam != IntPtr.Zero:
                SessionEnding?.Invoke(((long)message.LParam & Native.EndSessionLogoff) != 0);
                message.Result = IntPtr.Zero;
                return;
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (lidNotification != IntPtr.Zero)
        {
            Native.UnregisterPowerSettingNotification(lidNotification);
            lidNotification = IntPtr.Zero;
        }
        Native.ShutdownBlockReasonDestroy(Handle);
        DestroyHandle();
    }
}
