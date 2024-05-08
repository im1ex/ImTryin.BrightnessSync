using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using ImTryin.BrightnessSync.Api;
using ImTryin.BrightnessSync.AppSettings;
using ImTryin.WindowsConsoleService;

namespace ImTryin.BrightnessSync;

public class ActualService : IActualService
{
    private List<MonitorOptions>? _appSettings;
    private MonitorCollection? _monitorCollection;
    private bool _refresh;
    private Timer? _timer;

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

        _monitorCollection = new MonitorCollection();

        _timer = new Timer(OnSync);

        _monitorCollection.BrightnessChanged += OnBrightnessChanged;
        _monitorCollection.DeviceChanged += OnDeviceChanged;

        OnSync(null);

        return true;
    }

    public void Stop()
    {
        if (_monitorCollection != null)
        {
            _monitorCollection.BrightnessChanged -= OnBrightnessChanged;
            _monitorCollection.DeviceChanged -= OnDeviceChanged;
        }

        if (_timer != null)
        {
            _timer.Change(-1, -1);
            _timer.Dispose();
            _timer = null;
        }

        if (_monitorCollection != null)
        {
            _monitorCollection.Dispose();
            _monitorCollection = null;
        }
    }

    private void GenerateDummyConfig(string appSettingsPath)
    {
        var appSettings = new List<MonitorOptions>();

        using var monitorCollection = new MonitorCollection();
        foreach (var monitorInstance in monitorCollection.MonitorInstances)
        {
            appSettings.Add(new MonitorOptions
            {
                ManufacturerName = monitorInstance.ManufacturerName,
                ProductCodeId = monitorInstance.ProductCodeId,
                SerialNumberId = monitorInstance.SerialNumberId
            });
        }

        using (var fileStream = File.Create(appSettingsPath))
            JsonSerializer.Serialize(fileStream, appSettings, new JsonSerializerOptions {WriteIndented = true});

        Console.WriteLine("Dummy config file generated. Tune it and re-run application.");
    }

    private void OnBrightnessChanged(object sender, MonitorInstance e)
    {
        Console.WriteLine("{0:O} [BrightnessChanged] Waiting 0.5 sec before sync...", DateTime.Now);

        _timer!.Change(500, -1);
    }

    private void OnDeviceChanged(object sender, EventArgs e)
    {
        Console.WriteLine("{0:O} [DeviceChanged] Waiting 3 sec before sync...", DateTime.Now);

        _refresh = true;

        _timer!.Change(3000, -1);
    }

    private void OnSync(object? state)
    {
        Console.WriteLine("{0:O} [OnSync] Started...", DateTime.Now);

        if (_refresh)
        {
            _monitorCollection!.Refresh();

            _refresh = false;
        }

        var usedInstanceNames = new HashSet<string>();

        var optionsAndInstances = _appSettings!
            .Select(monitorOptions =>
            {
                var instances = new List<MonitorInstance>();

                foreach (var monitorInstance in _monitorCollection!.MonitorInstances)
                {
                    var instanceName = monitorInstance.InstanceName;

                    if (usedInstanceNames.Contains(instanceName))
                        continue;

                    if (monitorInstance.ManufacturerName.Contains(monitorOptions.ManufacturerName) &&
                        monitorInstance.ProductCodeId.Contains(monitorOptions.ProductCodeId) &&
                        monitorInstance.SerialNumberId.Contains(monitorOptions.SerialNumberId))
                    {
                        usedInstanceNames.Add(instanceName);
                        instances.Add(monitorInstance);
                    }
                }

                return new {MonitorOptions = monitorOptions, Instances = instances};
            })
            .ToList();

        if (optionsAndInstances.Count == 0 || optionsAndInstances[0].Instances.Count == 0)
            return;

        Console.WriteLine("{0:O} [OnSync] Getting main monitor brightness...", DateTime.Now);
        var mainOptions = optionsAndInstances[0].MonitorOptions;

        var mainInstance = optionsAndInstances[0].Instances[0];

        var currentBrightness = mainInstance.Brightness;

        Console.WriteLine("{0:O} [OnSync] Main monitor brightness set to {1}", DateTime.Now, currentBrightness);

        var newBrightness = currentBrightness < mainOptions.Min
            ? mainOptions.Min
            : mainOptions.Max < currentBrightness
                ? mainOptions.Max
                : currentBrightness;

        if (newBrightness != currentBrightness)
        {
            Console.WriteLine("{0:O} [OnSync] Updating main monitor brightness to {1}...", DateTime.Now, newBrightness);

            mainInstance.Brightness = newBrightness;
        }

        foreach (var optionsAndInstance in optionsAndInstances)
        {
            var anotherOptions = optionsAndInstance.MonitorOptions;

            var anotherBrightness = (byte) (anotherOptions.Min +
                                            (anotherOptions.Max - anotherOptions.Min) * (newBrightness - mainOptions.Min) /
                                            (mainOptions.Max - mainOptions.Min));

            foreach (var anotherInstance in optionsAndInstance.Instances)
            {
                if (anotherInstance != mainInstance)
                {
                    Console.WriteLine("{0:O} [OnSync] Updating another monitor brightness to {1}...", DateTime.Now, anotherBrightness);

                    anotherInstance.Brightness = anotherBrightness;
                }
            }
        }

        Console.WriteLine("{0:O} [OnSync] Finished!", DateTime.Now);
    }
}