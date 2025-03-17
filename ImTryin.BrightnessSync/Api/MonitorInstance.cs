namespace ImTryin.BrightnessSync.Api;

internal abstract class MonitorInstance
{
    protected MonitorInstance(string instanceName, string manufacturerName, string productCodeId, string serialNumberId, string userFriendlyName, bool isInternal)
    {
        InstanceName = instanceName;
        ManufacturerName = manufacturerName;
        ProductCodeId = productCodeId;
        SerialNumberId = serialNumberId;
        UserFriendlyName = userFriendlyName;
        IsInternal = isInternal;
    }

    public string InstanceName { get; }
    public string ManufacturerName { get; }
    public string ProductCodeId { get; }
    public string SerialNumberId { get; }
    public string UserFriendlyName { get; }
    public bool IsInternal { get; }

    public abstract byte Brightness { get; set; }
}