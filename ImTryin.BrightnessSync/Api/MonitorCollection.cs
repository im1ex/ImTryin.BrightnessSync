using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using ImTryin.BrightnessSync.Extensions;

namespace ImTryin.BrightnessSync.Api;

internal class MonitorCollection : IDisposable
{
    public MonitorCollection()
    {
        Refresh();
    }

    private readonly object _lock = new object();
    private readonly List<PhysicalMonitorDevice> _physicalMonitorDevices = new List<PhysicalMonitorDevice>();
    private readonly List<MonitorInstance> _monitorInstances = new List<MonitorInstance>();

    public IReadOnlyList<MonitorInstance> MonitorInstances
    {
        get
        {
            lock (_lock)
            {
                return new List<MonitorInstance>(_monitorInstances).AsReadOnly();
            }
        }
    }

    public void Refresh()
    {
        lock (_lock)
        {

            Cleanup();

            using var monitorIdSearcher = new ManagementObjectSearcher("\\\\.\\ROOT\\WMI", "SELECT * FROM WmiMonitorID");
            using var monitorIdCollection = monitorIdSearcher.Get();

            using var monitorBrightnessSearcher = new ManagementObjectSearcher("\\\\.\\ROOT\\WMI", "SELECT * FROM WmiMonitorBrightness");
            using var monitorBrightnessCollection = monitorBrightnessSearcher.Get();
            var monitorBrightnessObjects = monitorBrightnessCollection.Cast<ManagementObject>().ToList();

            using var monitorBrightnessMethodsSearcher = new ManagementObjectSearcher("\\\\.\\ROOT\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            using var monitorBrightnessMethodsCollection = monitorBrightnessMethodsSearcher.Get();
            var monitorBrightnessMethodsObjects = monitorBrightnessMethodsCollection.Cast<ManagementObject>().ToList();

            _physicalMonitorDevices.AddRange(MonitorApi.GetPhysicalMonitorDevices());

            foreach (var monitorIdObject in monitorIdCollection)
            {
                var instanceName = (string)monitorIdObject.Properties["InstanceName"].Value;

                var monitorBrightnessObject = monitorBrightnessObjects.SingleOrDefault(x => (string)x.Properties["InstanceName"].Value == instanceName);
                var monitorBrightnessMethodsObject =
                    monitorBrightnessMethodsObjects.SingleOrDefault(x => (string)x.Properties["InstanceName"].Value == instanceName);

                if (monitorBrightnessObject != null && monitorBrightnessMethodsObject != null)
                {
                    _monitorInstances.Add(new WmiMonitorInstance(
                        instanceName,
                        monitorIdObject.Properties["ManufacturerName"].ReadStringFromUInt16ArrayValue(),
                        monitorIdObject.Properties["ProductCodeID"].ReadStringFromUInt16ArrayValue(),
                        monitorIdObject.Properties["SerialNumberID"].ReadStringFromUInt16ArrayValue()));
                }
                else
                {
                    var deviceId = instanceName.EndsWith("_0")
                        ? "\\\\?\\" + instanceName.Substring(0, instanceName.Length - 2).Replace("\\", "#") + "#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"
                        : throw new NotSupportedException("Check this case!");

                    var physicalMonitorDevice = _physicalMonitorDevices.Find(x => x.DeviceId == deviceId);

                    if (physicalMonitorDevice != null)
                    {
                        _monitorInstances.Add(new PmdMonitorInstance(
                            instanceName,
                            monitorIdObject.Properties["ManufacturerName"].ReadStringFromUInt16ArrayValue(),
                            monitorIdObject.Properties["ProductCodeID"].ReadStringFromUInt16ArrayValue(),
                            monitorIdObject.Properties["SerialNumberID"].ReadStringFromUInt16ArrayValue(),
                            physicalMonitorDevice));
                    }
                }

                monitorIdObject.Dispose();
            }

            foreach (var monitorBrightnessObject in monitorBrightnessObjects)
                monitorBrightnessObject.Dispose();
            foreach (var monitorBrightnessMethodsObject in monitorBrightnessMethodsObjects)
                monitorBrightnessMethodsObject.Dispose();
        }
    }

    private EventHandler<MonitorInstance>? _brightnessChanged;

    private void OnBrightnessEventArrived(object sender, EventArrivedEventArgs e)
    {
        var monitorInstance = _monitorInstances.SingleOrDefault(x => x.InstanceName == (string)e.NewEvent.Properties["InstanceName"].Value);

        if (monitorInstance != null)
            _brightnessChanged?.Invoke(this, monitorInstance);
    }

    private ManagementEventWatcher? _brightnessEventWatcher;

    public event EventHandler<MonitorInstance> BrightnessChanged
    {
        add
        {
            _brightnessChanged += value;

            if (_brightnessEventWatcher == null)
            {
                _brightnessEventWatcher = new ManagementEventWatcher("\\\\.\\ROOT\\WMI", "SELECT * FROM WmiMonitorBrightnessEvent");
                _brightnessEventWatcher.EventArrived += OnBrightnessEventArrived;
                _brightnessEventWatcher.Start();
            }
        }
        remove
        {
            _brightnessChanged -= value;

            if (_brightnessChanged == null && _brightnessEventWatcher != null)
            {
                _brightnessEventWatcher.Stop();
                _brightnessEventWatcher.EventArrived -= OnBrightnessEventArrived;
                _brightnessEventWatcher.Dispose();
                _brightnessEventWatcher = null;
            }
        }
    }

    private EventHandler? _deviceChanged;

    private void OnDeviceChangeEventArrived(object sender, EventArrivedEventArgs e)
    {
        _deviceChanged?.Invoke(this, EventArgs.Empty);
    }

    private ManagementEventWatcher? _deviceChangeEventWatcher;

    public event EventHandler DeviceChanged
    {
        add
        {
            _deviceChanged += value;

            if (_deviceChangeEventWatcher == null)
            {
                _deviceChangeEventWatcher = new ManagementEventWatcher("\\\\.\\ROOT\\CIMV2", "SELECT * FROM Win32_DeviceChangeEvent");
                _deviceChangeEventWatcher.EventArrived += OnDeviceChangeEventArrived;
                _deviceChangeEventWatcher.Start();
            }
        }
        remove
        {
            _deviceChanged -= value;

            if (_deviceChanged == null && _deviceChangeEventWatcher != null)
            {
                _deviceChangeEventWatcher.Stop();
                _deviceChangeEventWatcher.EventArrived -= OnDeviceChangeEventArrived;
                _deviceChangeEventWatcher.Dispose();
                _deviceChangeEventWatcher = null;
            }
        }
    }

    private void Cleanup()
    {
        _monitorInstances.Clear();

        foreach (var physicalMonitorDevice in _physicalMonitorDevices)
            MonitorApi.DestroyPhysicalMonitorInternal(physicalMonitorDevice.PhysicalMonitorHandle);
        _physicalMonitorDevices.Clear();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            Cleanup();
        }
    }
}