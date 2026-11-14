using System;
using System.Collections.Generic;
using System.Linq;

namespace MacExtend.Core
{
    public enum ResolutionMode
    {
        Auto,
        Custom,
    }

    /// <summary>User-editable settings. Every value is kept inside its valid range by <see cref="Clamp"/>.</summary>
    public sealed class AppSettings
    {
        public const int MinWidth = 640, MaxWidth = 4096;
        public const int MinHeight = 480, MaxHeight = 2304;
        public const int MinRefresh = 24, MaxRefresh = 240;

        /// <summary>Name shown in the Mac's Screen Mirroring menu. Empty means "derive from the machine name".</summary>
        public string DeviceName { get; set; } = "";
        public ResolutionMode ResolutionMode { get; set; } = ResolutionMode.Auto;
        public int CustomWidth { get; set; } = 1920;
        public int CustomHeight { get; set; } = 1080;
        public int CustomRefresh { get; set; } = 60;
        public bool CustomH265 { get; set; }
        /// <summary>Maximum frame rate requested from the Mac: 30 or 60.</summary>
        public int Fps { get; set; } = 60;
        public bool AudioEnabled { get; set; } = true;
        public bool PinEnabled { get; set; } = true;
        /// <summary>Fixed 4-digit PIN. Empty means "not generated yet".</summary>
        public string Pin { get; set; } = "";
        public bool StartWithWindows { get; set; }
        /// <summary>Look for a newer release on GitHub shortly after start and every few hours.</summary>
        public bool AutoCheckUpdates { get; set; } = true;
        /// <summary>Bring the AirPlay video window to the front as soon as the device starts using it.</summary>
        public bool FocusOnConnect { get; set; } = true;
        /// <summary>Hide the Windows mouse pointer while the AirPlay video is full screen and in front.</summary>
        public bool HideCursor { get; set; } = true;
        /// <summary>Open the AirPlay video full screen instead of in a window that can be moved and resized.</summary>
        public bool Fullscreen { get; set; }
        /// <summary>Keep the AirPlay video window above other apps while it is a normal or maximized window.</summary>
        public bool AlwaysOnTop { get; set; }
        /// <summary>Run the Bluetooth helper so the laptop can act as a keyboard and mouse for the device.</summary>
        public bool BluetoothControl { get; set; }
        /// <summary>Devices allowed to use "Điều khiển trực tiếp" over Wi-Fi (RemotePairing done), UDIDs joined by ';'.</summary>
        public string DirectWifiDevices { get; set; } = "";
        /// <summary>The device the last direct-control session used; the setup window looks at it first.</summary>
        public string DirectLastDevice { get; set; } = "";
        /// <summary>
        /// "Điều khiển trực tiếp": while an iPhone or iPad mirrors, its AirPlay picture takes clicks and keys through a
        /// CoreDevice session that starts by itself.
        /// </summary>
        public bool DirectControl { get; set; }
        /// <summary>The device picked for each AirPlay name, as <see cref="CoreDevice.RememberedDevices"/> keeps it.</summary>
        public string DirectDevices { get; set; } = "";

        public IReadOnlyList<string> WifiDevices() =>
            (DirectWifiDevices ?? "").Split(';').Where(CoreDevice.CoreDeviceOptions.IsValidUdid).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public void SetWifiDevice(string udid, bool on)
        {
            var list = WifiDevices().Where(u => !string.Equals(u, udid, StringComparison.OrdinalIgnoreCase)).ToList();
            if (on && CoreDevice.CoreDeviceOptions.IsValidUdid(udid)) list.Add(udid);
            DirectWifiDevices = string.Join(";", list);
        }

        public AppSettings Clone() => (AppSettings)MemberwiseClone();

        public void Clamp()
        {
            DeviceName = DeviceName ?? "";
            Pin = Pin ?? "";
            DirectWifiDevices = string.Join(";", WifiDevices());
            DirectLastDevice = CoreDevice.CoreDeviceOptions.IsValidUdid(DirectLastDevice) ? DirectLastDevice : "";
            DirectDevices = CoreDevice.RememberedDevices.Parse(DirectDevices).Count == 0 ? "" : DirectDevices;
            if (!Enum.IsDefined(typeof(ResolutionMode), ResolutionMode)) ResolutionMode = ResolutionMode.Auto;
            CustomWidth = Math.Min(MaxWidth, Math.Max(MinWidth, CustomWidth));
            CustomHeight = Math.Min(MaxHeight, Math.Max(MinHeight, CustomHeight));
            CustomRefresh = Math.Min(MaxRefresh, Math.Max(MinRefresh, CustomRefresh));
            Fps = Fps == 30 ? 30 : 60;
        }
    }
}
