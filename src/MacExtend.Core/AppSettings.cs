using System;

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

        public AppSettings Clone() => (AppSettings)MemberwiseClone();

        public void Clamp()
        {
            DeviceName = DeviceName ?? "";
            Pin = Pin ?? "";
            if (!Enum.IsDefined(typeof(ResolutionMode), ResolutionMode)) ResolutionMode = ResolutionMode.Auto;
            CustomWidth = Math.Min(MaxWidth, Math.Max(MinWidth, CustomWidth));
            CustomHeight = Math.Min(MaxHeight, Math.Max(MinHeight, CustomHeight));
            CustomRefresh = Math.Min(MaxRefresh, Math.Max(MinRefresh, CustomRefresh));
            Fps = Fps == 30 ? 30 : 60;
        }
    }
}
