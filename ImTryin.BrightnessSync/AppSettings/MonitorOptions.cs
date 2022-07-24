namespace BrightnessSync.AppSettings;

public class MonitorOptions
{
    public string ManufacturerName { get; set; } = string.Empty;
    public string ProductCodeId { get; set; } = string.Empty;
    public string SerialNumberId { get; set; } = string.Empty;

    public byte Min { get; set; } = 0;
    public byte Max { get; set; } = 100;
}