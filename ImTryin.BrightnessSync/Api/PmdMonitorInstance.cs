namespace ImTryin.BrightnessSync.Api;

internal class PmdMonitorInstance : MonitorInstance
{
    public PmdMonitorInstance(string instanceName, string manufacturerName, string productCodeId, string serialNumberId,
        PhysicalMonitorDevice physicalMonitorDevice)
        : base(instanceName, manufacturerName, productCodeId, serialNumberId)
    {
        _physicalMonitorDevice = physicalMonitorDevice;
    }

    private readonly PhysicalMonitorDevice _physicalMonitorDevice;

    public override byte Brightness
    {
        get
        {
            MonitorApi.GetMonitorBrightness(_physicalMonitorDevice.PhysicalMonitorHandle, out _, out var currentBrightness, out _);
            return (byte)currentBrightness;
        }
        set { MonitorApi.SetMonitorBrightness(_physicalMonitorDevice.PhysicalMonitorHandle, value); }
    }
}