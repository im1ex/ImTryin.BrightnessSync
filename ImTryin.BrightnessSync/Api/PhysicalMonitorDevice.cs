using System;

namespace BrightnessSync.Api;

internal class PhysicalMonitorDevice
{
    public PhysicalMonitorDevice(string deviceId, IntPtr physicalMonitorHandle, string capabilitiesString)
    {
        DeviceId = deviceId;
        PhysicalMonitorHandle = physicalMonitorHandle;
        CapabilitiesString = capabilitiesString;
    }

    public string DeviceId { get; }

    public IntPtr PhysicalMonitorHandle { get; }

    public string CapabilitiesString { get; }
}