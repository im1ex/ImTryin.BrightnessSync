namespace ImTryin.BrightnessSync.Api;

internal class PmdMonitorInstance : MonitorInstance
{
    public PmdMonitorInstance(string instanceName, string manufacturerName, string productCodeId, string serialNumberId, string userFriendlyName, bool isInternal,
        PhysicalMonitorDevice physicalMonitorDevice)
        : base(instanceName, manufacturerName, productCodeId, serialNumberId, userFriendlyName, isInternal)
    {
        _physicalMonitorDevice = physicalMonitorDevice;
    }

    private readonly PhysicalMonitorDevice _physicalMonitorDevice;

    public override byte Brightness
    {
        get { return (byte)MonitorApi.GetMonitorBrightnessWithRetries(_physicalMonitorDevice.PhysicalMonitorHandle).Current; }
        set { MonitorApi.SetMonitorBrightnessWithRetries(_physicalMonitorDevice.PhysicalMonitorHandle, value); }
    }
}