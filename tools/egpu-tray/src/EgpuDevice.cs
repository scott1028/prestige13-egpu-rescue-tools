using System;
using System.Management;
using Microsoft.Win32;

namespace EgpuTray
{
    internal sealed class EgpuState
    {
        public bool HasRouter;
        public bool HasGpu;
        public int GpuCode;
        public string GpuInstanceId;

        public bool IsConnected { get { return HasRouter || HasGpu; } }
        public bool IsGpuOk { get { return HasGpu && GpuCode == 0; } }

        // Keep in sync with Format-EgpuState in tools/egpu-connect/egpu-connect.ps1
        public string Describe()
        {
            if (!IsConnected) return "未連接";
            if (!HasGpu) return "USB4 Router 已出現，GPU 尚未出現";
            if (GpuCode == 0) return "GPU 正常";
            return "GPU 異常（Code " + GpuCode + "）";
        }
    }

    internal static class EgpuDevice
    {
        // WQL LIKE patterns: GC34X USB4 router and RTX 5090 Laptop GPU.
        // Keep in sync with tools/egpu-connect/egpu-connect.ps1
        private const string RouterIdPattern = @"USB4\\VID_8087&PID_5786%";
        private const string GpuIdPattern = @"PCI\\VEN_10DE&DEV_2C18%";

        private const int CodeDisabled = 22;
        private const int CsConfigFlagDisabled = 0x1;
        private const string ProfileEnumKey = @"SYSTEM\CurrentControlSet\Hardware Profiles\Current\System\CurrentControlSet\Enum\";

        public static EgpuState ReadState()
        {
            var state = new EgpuState();
            string query = "SELECT DeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE DeviceID LIKE '"
                + RouterIdPattern + "' OR DeviceID LIKE '" + GpuIdPattern + "'";
            using (var searcher = new ManagementObjectSearcher(query))
            using (var devices = searcher.Get())
            {
                foreach (ManagementObject device in devices)
                {
                    using (device)
                    {
                        string id = (string)device["DeviceID"];
                        if (id.StartsWith(@"USB4\", StringComparison.OrdinalIgnoreCase))
                        {
                            state.HasRouter = true;
                        }
                        else if (!state.HasGpu)
                        {
                            state.HasGpu = true;
                            state.GpuInstanceId = id;
                            state.GpuCode = Convert.ToInt32(device["ConfigManagerErrorCode"]);
                        }
                    }
                }
            }
            return state;
        }

        // NVIDIA "Disconnect GPU" disables the GPU in the current hardware profile.
        // That Code 22 survives replug, and Enable-PnpDevice does not clear it.
        public static bool GetIsDisabledByProfile(EgpuState state)
        {
            if (!state.HasGpu || state.GpuCode != CodeDisabled) return false;
            using (var key = Registry.LocalMachine.OpenSubKey(ProfileEnumKey + state.GpuInstanceId))
            {
                return key != null && (Convert.ToInt32(key.GetValue("CSConfigFlags", 0)) & CsConfigFlagDisabled) != 0;
            }
        }

        public static void ReEnableGpu(string instanceId)
        {
            using (var key = Registry.LocalMachine.OpenSubKey(ProfileEnumKey + instanceId, true))
            {
                if (key != null)
                {
                    int flags = Convert.ToInt32(key.GetValue("CSConfigFlags", 0));
                    key.SetValue("CSConfigFlags", flags & ~CsConfigFlagDisabled, RegistryValueKind.DWord);
                }
            }
            Program.RunHidden("pnputil.exe", "/enable-device \"" + instanceId + "\"");
        }
    }
}
