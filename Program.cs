using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "MonitorToggle.log");

    [STAThread]
    private static int Main()
    {
        try
        {
            Log("---- run ----");

            uint activePaths = DisplayConfigApi.GetActivePathCount();
            Log($"Active paths: {activePaths}");

            if (activePaths >= 2)
            {
                // Extended -> Show only on 1
                if (!DisplayConfigApi.TrySetTopologyInternal(out int err))
                {
                    Log($"SetDisplayConfig(INTERNAL) failed: {err}. Falling back to DisplaySwitch.exe /internal");
                    ShellDisplaySwitch("/internal");
                }

                return 0;
            }

            // Single display -> Extend
            if (!DisplayConfigApi.TrySetTopologyExtend(out int err2))
            {
                Log($"SetDisplayConfig(EXTEND) failed: {err2}. Falling back to DisplaySwitch.exe /extend");
                ShellDisplaySwitch("/extend");
            }

            // After switching to extend, restore the non-primary's last registry mode/position.
            // This is best-effort; topology is the real "extend vs show only" switch.
            var devices = DisplayApi.GetDisplayDevices().ToList();
            var secondary = devices
                .Where(d => !d.IsPrimary)
                .OrderByDescending(d => d.AttachedToDesktop)
                .FirstOrDefault();

            if (secondary is null)
            {
                Log("No secondary device found.");
                return 3;
            }

            var regMode = DisplayApi.GetRegistryDevMode(secondary.DeviceName);
            Log($"Secondary device: {secondary.DeviceName} ({secondary.DeviceString}), attached={secondary.AttachedToDesktop}");
            Log($"Registry mode: {regMode.dmPelsWidth}x{regMode.dmPelsHeight} @{regMode.dmDisplayFrequency}Hz pos=({regMode.dmPosition.x},{regMode.dmPosition.y}) fields=0x{regMode.dmFields:X}");

            if (regMode.dmPelsWidth > 0 && regMode.dmPelsHeight > 0)
            {
                DisplayApi.ApplyDevMode(secondary.DeviceName, regMode);
                Log("Applied registry devmode to secondary.");
            }
            else
            {
                Log("Registry mode not usable; skipping ApplyDevMode.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex);
            return 100;
        }
    }

    private static void ShellDisplaySwitch(string args)
    {
        // Built-in Windows helper that reliably switches topology.
        // /internal = Show only on 1
        // /extend   = Extend these displays
        var psi = new ProcessStartInfo
        {
            FileName = "DisplaySwitch.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi);
        p?.WaitForExit(5000);
    }

    private static void Log(string msg)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { }
    }

    private static class DisplayConfigApi
    {
        // Query flags
        private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;

        // SetDisplayConfig flags (topology-only form)
        // IMPORTANT: Do NOT use SDC_USE_DATABASE_CURRENT here; it can trigger ERROR_INVALID_PARAMETER (87).
        private const uint SDC_APPLY = 0x00000080;
        private const uint SDC_SAVE_TO_DATABASE = 0x00000200;
        private const uint SDC_ALLOW_CHANGES = 0x00000400;

        // Topology flags
        private const uint SDC_TOPOLOGY_INTERNAL = 0x00000001; // "Show only on 1"
        private const uint SDC_TOPOLOGY_EXTEND   = 0x00000004; // "Extend these displays"

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(
            uint flags,
            out uint numPathArrayElements,
            out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(
            uint numPathArrayElements,
            IntPtr pathArray,
            uint numModeInfoArrayElements,
            IntPtr modeInfoArray,
            uint flags);

        public static uint GetActivePathCount()
        {
            int r = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out _);
            if (r != 0) throw new InvalidOperationException($"GetDisplayConfigBufferSizes failed: {r}");
            return pathCount;
        }

        public static bool TrySetTopologyInternal(out int error)
        {
            uint flags = SDC_APPLY | SDC_SAVE_TO_DATABASE | SDC_ALLOW_CHANGES | SDC_TOPOLOGY_INTERNAL;
            int r = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, flags);
            error = r;
            return r == 0;
        }

        public static bool TrySetTopologyExtend(out int error)
        {
            uint flags = SDC_APPLY | SDC_SAVE_TO_DATABASE | SDC_ALLOW_CHANGES | SDC_TOPOLOGY_EXTEND;
            int r = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, flags);
            error = r;
            return r == 0;
        }
    }

    private static class DisplayApi
    {
        private const int ENUM_REGISTRY_SETTINGS = -2;

        private const int DM_POSITION = 0x00000020;
        private const int DM_PELSWIDTH = 0x00080000;
        private const int DM_PELSHEIGHT = 0x00100000;
        private const int DM_DISPLAYFREQUENCY = 0x00400000;
        private const int DM_DISPLAYFLAGS = 0x00200000;
        private const int DM_BITSPERPEL = 0x00040000;

        private const int CDS_UPDATEREGISTRY = 0x00000001;
        private const int CDS_NORESET = 0x10000000;

        private const int DISP_CHANGE_SUCCESSFUL = 0;

        [Flags]
        private enum DisplayDeviceStateFlags : int
        {
            AttachedToDesktop = 0x1,
            PrimaryDevice = 0x4,
            MirroringDriver = 0x8
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;

            public DisplayDeviceStateFlags StateFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINTL
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DEVMODE
        {
            private const int CCHDEVICENAME = 32;
            private const int CCHFORMNAME = 32;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
            public string dmDeviceName;

            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;

            public POINTL dmPosition;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;

            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
            public string dmFormName;

            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;

            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;

            public int dmPanningWidth;
            public int dmPanningHeight;

            public static DEVMODE Create()
            {
                return new DEVMODE
                {
                    dmDeviceName = new string('\0', CCHDEVICENAME),
                    dmFormName = new string('\0', CCHFORMNAME),
                    dmSize = (short)Marshal.SizeOf(typeof(DEVMODE))
                };
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);

        internal sealed record DeviceInfo(string DeviceName, string DeviceString, bool AttachedToDesktop, bool IsPrimary);

        internal static IEnumerable<DeviceInfo> GetDisplayDevices()
        {
            uint devNum = 0;
            while (true)
            {
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                if (!EnumDisplayDevices(null, devNum, ref dd, 0))
                    yield break;

                if (dd.StateFlags.HasFlag(DisplayDeviceStateFlags.MirroringDriver))
                {
                    devNum++;
                    continue;
                }

                bool attached = dd.StateFlags.HasFlag(DisplayDeviceStateFlags.AttachedToDesktop);
                bool primary = dd.StateFlags.HasFlag(DisplayDeviceStateFlags.PrimaryDevice);

                yield return new DeviceInfo(dd.DeviceName, dd.DeviceString, attached, primary);
                devNum++;
            }
        }

        internal static DEVMODE GetRegistryDevMode(string deviceName)
        {
            var dm = DEVMODE.Create();
            if (!EnumDisplaySettings(deviceName, ENUM_REGISTRY_SETTINGS, ref dm))
                return DEVMODE.Create();
            return dm;
        }

        internal static void ApplyDevMode(string deviceName, DEVMODE mode)
        {
            var dm = mode;
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));

            dm.dmFields |= DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT;
            if (dm.dmDisplayFrequency > 0) dm.dmFields |= DM_DISPLAYFREQUENCY;
            if (dm.dmBitsPerPel > 0) dm.dmFields |= DM_BITSPERPEL;
            if (dm.dmDisplayFlags != 0) dm.dmFields |= DM_DISPLAYFLAGS;

            int r1 = ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero);
            if (r1 != DISP_CHANGE_SUCCESSFUL)
                throw new InvalidOperationException($"ChangeDisplaySettingsEx(device) failed: {r1}");

            int r2 = ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
            if (r2 != DISP_CHANGE_SUCCESSFUL)
                throw new InvalidOperationException($"ChangeDisplaySettingsEx(global) failed: {r2}");
        }
    }
}
