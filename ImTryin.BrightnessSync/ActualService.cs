using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using BrightnessSync.Api;
using BrightnessSync.AppSettings;
using BrightnessSync.Extensions;
using ImTryin.WindowsConsoleService;

namespace ImTryin.BrightnessSync;

public class ActualService : IActualService
{
    private List<MonitorOptions>? _appSettings;
    private Timer? _timer;
    private ManagementEventWatcher? _deviceChangeEventWatcher;
    private ManagementEventWatcher? _brightnessEventWatcher;

    public bool Start(bool runningAsService)
    {
        var appSettingsPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "appsettings.json");
        if (!File.Exists(appSettingsPath))
        {
            GenerateDummyConfig(appSettingsPath);

            return false;
        }

        using (var fileStream = File.OpenRead(appSettingsPath))
            _appSettings = JsonSerializer.Deserialize<List<MonitorOptions>>(fileStream);

        if (_appSettings == null || _appSettings.Count == 0)
        {
            GenerateDummyConfig(appSettingsPath);

            return false;
        }

        // ToDo: validate app settings more?

        _timer = new Timer(OnSync);

        _deviceChangeEventWatcher = new ManagementEventWatcher("\\\\.\\ROOT\\CIMV2", "SELECT * FROM Win32_DeviceChangeEvent");
        _deviceChangeEventWatcher.EventArrived += OnDeviceChangeEvent;
        _deviceChangeEventWatcher.Start();

        _brightnessEventWatcher = new ManagementEventWatcher("\\\\.\\ROOT\\WMI", "SELECT * FROM WmiMonitorBrightnessEvent");
        _brightnessEventWatcher.EventArrived += OnBrightnessEvent;
        _brightnessEventWatcher.Start();

        OnSync(null);

        return true;
    }

    public void Stop()
    {
        if (_brightnessEventWatcher != null)
        {
            _brightnessEventWatcher.Stop();
            _brightnessEventWatcher.Dispose();
            _brightnessEventWatcher = null;
        }

        if (_deviceChangeEventWatcher != null)
        {
            _deviceChangeEventWatcher.Stop();
            _deviceChangeEventWatcher.Dispose();
            _deviceChangeEventWatcher = null;
        }

        if (_timer != null)
        {
            _timer.Change(-1, -1);
            _timer.Dispose();
            _timer = null;
        }
    }

    private void GenerateDummyConfig(string appSettingsPath)
    {
        var appSettings = new List<MonitorOptions>();

        using var monitorIdSearcher = new ManagementObjectSearcher("\\\\.\\ROOT\\WMI", "SELECT * FROM WmiMonitorID");
        using var monitorIdObjects = monitorIdSearcher.Get();
        foreach (var monitorIdObject in monitorIdObjects)
        {
            appSettings.Add(new MonitorOptions
            {
                ManufacturerName = monitorIdObject.Properties["ManufacturerName"].ReadStringFromUInt16ArrayValue(),
                ProductCodeId = monitorIdObject.Properties["ProductCodeID"].ReadStringFromUInt16ArrayValue(),
                SerialNumberId = monitorIdObject.Properties["SerialNumberID"].ReadStringFromUInt16ArrayValue()
            });
        }

        using (var fileStream = File.Create(appSettingsPath))
            JsonSerializer.Serialize(fileStream, appSettings, new JsonSerializerOptions {WriteIndented = true});

        Console.WriteLine("Dummy config file generated. Tune it and re-run application.");
    }

    private void OnDeviceChangeEvent(object sender, EventArrivedEventArgs e)
    {
        var properties = e.NewEvent.Properties;
        Console.WriteLine("{0:O} OnDeviceChangeEvent {1}", properties["TIME_CREATED"].ReadDateTimeFromUInt64(), properties["EventType"].Value);

        MonitorApi.ClearDeviceCapabilitiesStringCache();

        _timer!.Change(3000, -1);
    }

    private void OnBrightnessEvent(object sender, EventArrivedEventArgs e)
    {
        var properties = e.NewEvent.Properties;
        Console.WriteLine("{0:O} OnBrightnessEvent {1} = {2}", properties["TIME_CREATED"].ReadDateTimeFromUInt64(), properties["InstanceName"].Value,
            properties["Brightness"].Value);

        _timer!.Change(500, -1);
    }

    private void OnSync(object? state)
    {
        Console.WriteLine("{0:O} OnSync started...", DateTime.Now);

        Console.WriteLine("{0:O} [OnSync] Enumerating WMI monitors...", DateTime.Now);

        using var monitorIdSearcher = new ManagementObjectSearcher("\\\\.\\ROOT\\WMI", "SELECT * FROM WmiMonitorID");
        using var monitorIdCollection = monitorIdSearcher.Get();

        var usedInstanceNames = new HashSet<string>();

        var optionsAndInstanceNames = _appSettings
            .Select(monitorOptions =>
            {
                var instanceNames = new List<string>();

                foreach (var monitorIdObject in monitorIdCollection)
                {
                    var instanceName = (string) monitorIdObject.Properties["InstanceName"].Value;

                    if (usedInstanceNames.Contains(instanceName))
                        continue;

                    if (monitorIdObject.Properties["ManufacturerName"].ReadStringFromUInt16ArrayValue().Contains(monitorOptions.ManufacturerName) &&
                        monitorIdObject.Properties["ProductCodeID"].ReadStringFromUInt16ArrayValue().Contains(monitorOptions.ProductCodeId) &&
                        monitorIdObject.Properties["SerialNumberID"].ReadStringFromUInt16ArrayValue().Contains(monitorOptions.SerialNumberId))
                    {
                        usedInstanceNames.Add(instanceName);
                        instanceNames.Add(instanceName);
                    }
                }

                return new {MonitorOptions = monitorOptions, InstanceNames = instanceNames};
            })
            .ToList();

        if (optionsAndInstanceNames[0].InstanceNames.Count == 0)
            return;

        Console.WriteLine("{0:O} [OnSync] Enumerating Win32 API monitors...", DateTime.Now);
        var physicalMonitorDevices = MonitorApi.GetPhysicalMonitorDevices();

        Console.WriteLine("{0:O} [OnSync] Getting main monitor brightness...", DateTime.Now);
        var mainOptions = optionsAndInstanceNames[0].MonitorOptions;

        var mainInstanceName = optionsAndInstanceNames[0].InstanceNames[0];

        var currentBrightness = GetBrightness(mainInstanceName, physicalMonitorDevices);
        var newBrightness = currentBrightness < mainOptions.Min
            ? mainOptions.Min
            : mainOptions.Max < currentBrightness
                ? mainOptions.Max
                : currentBrightness;

        if (newBrightness != currentBrightness)
        {
            Console.WriteLine("{0:O} [OnSync] Updating main monitor brightness to {1}...", DateTime.Now, newBrightness);

            SetBrightness(mainInstanceName, newBrightness, physicalMonitorDevices);
        }

        foreach (var optionsAndInstanceName in optionsAndInstanceNames)
        {
            var anotherOptions = optionsAndInstanceName.MonitorOptions;

            var anotherBrightness = (byte) (anotherOptions.Min +
                                            (anotherOptions.Max - anotherOptions.Min) * (newBrightness - mainOptions.Min) /
                                            (mainOptions.Max - mainOptions.Min));

            foreach (var anotherInstanceName in optionsAndInstanceName.InstanceNames)
            {
                if (anotherInstanceName != mainInstanceName)
                {
                    Console.WriteLine("{0:O} [OnSync] Updating another monitor brightness to {1}...", DateTime.Now, anotherBrightness);

                    SetBrightness(anotherInstanceName, anotherBrightness, physicalMonitorDevices);
                }
            }
        }

        foreach (var physicalMonitorDevice in physicalMonitorDevices)
            MonitorApi.DestroyPhysicalMonitorInternal(physicalMonitorDevice.PhysicalMonitorHandle);

        Console.WriteLine("{0:O} OnSync finished!", DateTime.Now);
    }

    private byte GetBrightness(string instanceName, List<PhysicalMonitorDevice> physicalMonitorDevices)
    {
        using var monitorBrightnessSearcher = new ManagementObjectSearcher("\\\\.\\ROOT\\WMI",
            "SELECT * FROM WmiMonitorBrightness WHERE InstanceName='" + instanceName.Replace("\\", "\\\\") + "'");

        using var monitorBrightnessCollection = monitorBrightnessSearcher.Get();

        foreach (ManagementBaseObject monitorBrightnessObject in monitorBrightnessCollection)
        {
            var currentBrightness = (byte) monitorBrightnessObject.Properties["CurrentBrightness"].Value;

            monitorBrightnessObject.Dispose();

            return currentBrightness;
        }


        var physicalMonitorDevice = FindPhysicalMonitorInfo(instanceName, physicalMonitorDevices);

        if (physicalMonitorDevice != null && physicalMonitorDevice.CapabilitiesString != string.Empty)
        {
            MonitorApi.GetMonitorBrightness(physicalMonitorDevice.PhysicalMonitorHandle, out _, out var currentBrightness, out _);

            return (byte) currentBrightness;
        }

        return 0;
    }

    private void SetBrightness(string instanceName, byte brightness, List<PhysicalMonitorDevice> physicalMonitorDevices)
    {
        using var monitorBrightnessMethodsSearcher = new ManagementObjectSearcher("\\\\.\\ROOT\\WMI",
            "SELECT * FROM WmiMonitorBrightnessMethods WHERE InstanceName='" + instanceName.Replace("\\", "\\\\") + "'");

        using var monitorBrightnessMethodsCollection = monitorBrightnessMethodsSearcher.Get();

        foreach (ManagementObject monitorBrightnessMethodsObject in monitorBrightnessMethodsCollection)
        {
            monitorBrightnessMethodsObject.InvokeMethod("WmiSetBrightness", new object[]
            {
                /* timeout in seconds */ 1, brightness
            });

            monitorBrightnessMethodsObject.Dispose();

            return;
        }

        var physicalMonitorDevice = FindPhysicalMonitorInfo(instanceName, physicalMonitorDevices);

        if (physicalMonitorDevice != null && physicalMonitorDevice.CapabilitiesString != string.Empty)
            MonitorApi.SetMonitorBrightness(physicalMonitorDevice.PhysicalMonitorHandle, brightness);
    }

    private PhysicalMonitorDevice? FindPhysicalMonitorInfo(string instanceName, List<PhysicalMonitorDevice> physicalMonitorDevices)
    {
        var deviceId = instanceName.EndsWith("_0")
            ? "\\\\?\\" + instanceName.Substring(0, instanceName.Length - 2).Replace("\\", "#") + "#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"
            : throw new NotSupportedException("Check this case!");

        return physicalMonitorDevices.Find(x => x.DeviceId == deviceId);
    }
}