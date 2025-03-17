using System.Collections.Generic;

namespace ImTryin.BrightnessSync.Settings;

public class AppSettings
{
    public const string InternalMonitorKey = "Internal";
    public const string DefaultMonitorKey = "Default";

    public Dictionary<string, MonitorSettings> Monitors { get; } = new Dictionary<string, MonitorSettings>();

    public Dictionary<string, Dictionary<string, ProfileSettings>> Profiles { get; } = new Dictionary<string, Dictionary<string, ProfileSettings>>();
}

public class MonitorSettings
{
    public string ManufacturerName { get; set; } = string.Empty;
    public string ProductCodeId { get; set; } = string.Empty;
    public string SerialNumberId { get; set; } = string.Empty;
}

public class ProfileSettings
{
    public static readonly ProfileSettings Default = new ProfileSettings { Min = 0, Max = 100 };

    public byte Min { get; set; }
    public byte Max { get; set; }
}
