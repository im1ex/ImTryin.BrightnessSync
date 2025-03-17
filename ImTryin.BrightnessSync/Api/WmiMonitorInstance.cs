using System.Management;

namespace ImTryin.BrightnessSync.Api;

internal class WmiMonitorInstance : MonitorInstance
{
    public WmiMonitorInstance(string instanceName, string manufacturerName, string productCodeId, string serialNumberId, string userFriendlyName, bool isInternal)
        : base(instanceName, manufacturerName, productCodeId, serialNumberId, userFriendlyName, isInternal)
    {
    }

    public override byte Brightness
    {
        get
        {
            using var monitorBrightnessObject
                = new ManagementObject(@"\\.\ROOT\WMI:WmiMonitorBrightness.InstanceName=""" + InstanceName.Replace(@"\", @"\\") + @"""");

            return (byte)monitorBrightnessObject.Properties["CurrentBrightness"].Value;
        }
        set
        {
            using var monitorBrightnessMethodsObject
                = new ManagementObject(@"\\.\ROOT\WMI:WmiMonitorBrightnessMethods.InstanceName=""" + InstanceName.Replace(@"\", @"\\") + @"""");

            monitorBrightnessMethodsObject.InvokeMethod("WmiSetBrightness", new object[] { 1 /* timeout in seconds */, value });
        }
    }
}