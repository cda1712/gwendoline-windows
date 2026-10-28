using System.Runtime.InteropServices;

namespace Gwendoline;

/// <summary>The Win32 calls Gwendoline needs, none of them privileged.</summary>
internal static partial class Native
{
    // Power plans (powrprof.dll). Every function returns a Win32 error code, 0 on success.

    [DllImport("powrprof.dll")]
    public static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    public static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    public static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    public static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    public static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, uint value);

    [DllImport("powrprof.dll")]
    public static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, uint value);

    [DllImport("powrprof.dll")]
    public static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid, uint accessFlags, uint index, ref Guid buffer, ref uint bufferSize);

    public const uint AccessScheme = 16;
    public const uint ErrorNoMoreItems = 259;

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate, [MarshalAs(UnmanagedType.U1)] bool force, [MarshalAs(UnmanagedType.U1)] bool wakeupEventsDisabled);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr memory);

    // Idle sleep

    public const uint EsContinuous = 0x80000000;
    public const uint EsSystemRequired = 0x00000001;

    [DllImport("kernel32.dll")]
    public static extern uint SetThreadExecutionState(uint flags);

    // Battery

    [StructLayout(LayoutKind.Sequential)]
    public struct SystemPowerStatus
    {
        /// <summary>0 offline, 1 online, 255 unknown.</summary>
        public byte ACLineStatus;
        /// <summary>8 charging, 128 no battery, 255 unknown.</summary>
        public byte BatteryFlag;
        /// <summary>0 to 100, 255 unknown.</summary>
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        /// <summary>Seconds left, -1 unknown or plugged in.</summary>
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    // Lid, wake and session end, delivered to a window

    public const int WmPowerBroadcast = 0x0218;
    public const int WmQueryEndSession = 0x0011;
    public const int WmEndSession = 0x0016;
    public const int PbtApmResumeAutomatic = 0x0012;
    public const int PbtPowerSettingChange = 0x8013;
    public const long EndSessionLogoff = 0x80000000;

    public static readonly Guid LidSwitchStateChange = new("ba3e0f4d-b817-4094-a2d1-d56379e6a0f3");

    [StructLayout(LayoutKind.Sequential)]
    public struct PowerBroadcastSetting
    {
        public Guid PowerSetting;
        public uint DataLength;
        public uint Data;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSettingGuid, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShutdownBlockReasonCreate(IntPtr window, string reason);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShutdownBlockReasonDestroy(IntPtr window);

    // Displays

    public const uint QdcOnlyActivePaths = 0x00000002;

    // DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY values of a built-in screen.
    public const uint OutputInternal = 0x80000000;
    public const uint OutputDisplayPortEmbedded = 11;
    public const uint OutputUdiEmbedded = 13;

    /// <summary>72 bytes. A LUID is two 4-byte halves: as a long it would be 8-byte aligned and shift every field after it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayConfigPathInfo
    {
        // DISPLAYCONFIG_PATH_SOURCE_INFO
        public uint SourceAdapterIdLow;
        public int SourceAdapterIdHigh;
        public uint SourceId;
        public uint SourceModeInfoIdx;
        public uint SourceStatusFlags;
        // DISPLAYCONFIG_PATH_TARGET_INFO
        public uint TargetAdapterIdLow;
        public int TargetAdapterIdHigh;
        public uint TargetId;
        public uint TargetModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public uint RefreshRateNumerator;
        public uint RefreshRateDenominator;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint TargetStatusFlags;
        public uint Flags;
    }

    /// <summary>DISPLAYCONFIG_MODE_INFO, only counted, never read.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    public struct DisplayConfigModeInfo
    {
        public uint InfoType;
    }

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DisplayConfigPathInfo[] paths, ref uint modeCount, [Out] DisplayConfigModeInfo[] modes, IntPtr topologyId);
}
