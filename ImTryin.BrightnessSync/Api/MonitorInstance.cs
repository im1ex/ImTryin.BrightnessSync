namespace ImTryin.BrightnessSync.Api;

internal abstract class MonitorInstance
{
    protected MonitorInstance(string instanceName, string manufacturerName, string productCodeId, string serialNumberId)
    {
        InstanceName = instanceName;
        ManufacturerName = manufacturerName;
        ProductCodeId = productCodeId;
        SerialNumberId = serialNumberId;
    }

    public string InstanceName { get; }
    public string ManufacturerName { get; }
    public string ProductCodeId { get; }
    public string SerialNumberId { get; }

    public abstract byte Brightness { get; set; }
}