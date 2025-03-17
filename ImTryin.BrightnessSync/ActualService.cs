using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using ImTryin.BrightnessSync.Api;
using ImTryin.BrightnessSync.Settings;
using ImTryin.WindowsConsoleService;

namespace ImTryin.BrightnessSync;

public class ActualService : IActualService
{
    private AppSettings? _appSettings;
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
            _appSettings = JsonSerializer.Deserialize<AppSettings>(fileStream, new JsonSerializerOptions { PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate });

        if (_appSettings == null || (_appSettings.Monitors.Count == 0 && _appSettings.Profiles.Count == 0))
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
        var appSettings = new AppSettings();

        using var monitorCollection = new MonitorCollection();
        foreach (var monitorInstance in monitorCollection.MonitorInstances)
        {
            if (monitorInstance.IsInternal)
                continue;

            appSettings.Monitors.Add(monitorInstance.UserFriendlyName, new()
            {
                ManufacturerName = monitorInstance.ManufacturerName,
                ProductCodeId = monitorInstance.ProductCodeId,
                SerialNumberId = monitorInstance.SerialNumberId
            });
        }

        var profileKey = string.Join("+",
                monitorCollection.MonitorInstances
                    .Where(mi => !mi.IsInternal)
                    .Select(mi => mi.UserFriendlyName));
        var profile = monitorCollection.MonitorInstances
                    .Where(mi => !mi.IsInternal)
                    .ToDictionary(mi => mi.UserFriendlyName, _ => ProfileSettings.Default);
        profile.Add(AppSettings.InternalMonitorKey, ProfileSettings.Default);
        profile.Add(AppSettings.DefaultMonitorKey, ProfileSettings.Default);

        appSettings.Profiles.Add(profileKey, profile);

        using (var fileStream = File.Create(appSettingsPath))
            JsonSerializer.Serialize(fileStream, appSettings, new JsonSerializerOptions { WriteIndented = true });

        Console.WriteLine("Dummy config file generated. Tune it and re-run application.");
    }

    private void OnBrightnessChanged(object sender, MonitorInstance e)
    {
        Console.WriteLine("{0:O} [BrightnessChanged] Waiting 0.5 sec before sync...", DateTime.Now);

        _timer!.Change(500, -1);
    }

    private void OnDeviceChanged(object sender, EventArgs e)
    {
        Console.WriteLine("{0:O} [DeviceChanged] {1} Waiting 3 sec before sync...", DateTime.Now, e);

        _refresh = true;

        _timer!.Change(3000, -1);
    }

    private void OnSync(object? state)
    {
        Console.WriteLine("{0:O} [OnSync] Started...", DateTime.Now);

        var appSettings = _appSettings!;
        var monitorCollection = _monitorCollection!;

        if (_refresh)
        {
            monitorCollection.Refresh();

            _refresh = false;
        }

        if (monitorCollection.MonitorInstances.Count <= 1)
        {
            Console.WriteLine("{0:O} [OnSync] Only one (or zero) monitor exists, cancel sync...", DateTime.Now);
            return;
        }

        var monitors = monitorCollection.MonitorInstances.Select(mi => (
            MonitorInstance: mi,
            Key: (string?)appSettings.Monitors.FirstOrDefault(m =>
                mi.ManufacturerName.Contains(m.Value.ManufacturerName) &&
                mi.ProductCodeId.Contains(m.Value.ProductCodeId) &&
                mi.SerialNumberId.Contains(m.Value.SerialNumberId)).Key));

        var profile = appSettings.Profiles.Select(p => (
            Profile: p,
            FitScore: monitors.Select(m =>
            {
                if (m.MonitorInstance.IsInternal)
                    return p.Value.ContainsKey(AppSettings.InternalMonitorKey) ? 100 : 0;

                return m.Key != null && p.Value.ContainsKey(m.Key) ? 100 : p.Value.ContainsKey(AppSettings.DefaultMonitorKey) ? 10 : 0;
            })
            .Sum()))
            .OrderByDescending(x => x.FitScore)
            .First().Profile;

        ProfileSettings getProfileSettings(string monitorKey)
        {
            return profile.Value.TryGetValue(monitorKey, out var profileSetting)
                ? profileSetting
                : profile.Value.TryGetValue(AppSettings.DefaultMonitorKey, out profileSetting)
                ? profileSetting
                : new();
        }

        Console.WriteLine("{0:O} [OnSync] \"{1}\" profile selected. Getting main monitor brightness...", DateTime.Now, profile.Key);

        var internalMonitor = monitors.First(m => m.MonitorInstance.IsInternal);

        var currentBrightness = internalMonitor.MonitorInstance.Brightness;

        Console.WriteLine("{0:O} [OnSync] \"{1}\" profile selected. \"{2}\" monitor brightness set to {3}", DateTime.Now, profile.Key, internalMonitor.Key, currentBrightness);

        var internalProfileSettings = getProfileSettings(AppSettings.InternalMonitorKey);

        var newBrightness = currentBrightness < internalProfileSettings.Min
            ? internalProfileSettings.Min
            : internalProfileSettings.Max < currentBrightness
                ? internalProfileSettings.Max
                : currentBrightness;

        if (newBrightness != currentBrightness)
        {
            Console.WriteLine("{0:O} [OnSync] \"{1}\" profile selected. Updating \"{2}\" monitor brightness to {3}...", DateTime.Now, profile.Key, internalMonitor.Key, newBrightness);

            internalMonitor.MonitorInstance.Brightness = newBrightness;
        }

        foreach (var monitor in monitors)
        {
            if (monitor.MonitorInstance == internalMonitor.MonitorInstance)
                continue;

            var profileSettings = getProfileSettings(monitor.Key ?? AppSettings.DefaultMonitorKey);

            var anotherBrightness = (byte)(profileSettings.Min +
                                            (profileSettings.Max - profileSettings.Min) * (newBrightness - internalProfileSettings.Min) /
                                            (internalProfileSettings.Max - internalProfileSettings.Min));

            Console.WriteLine("{0:O} [OnSync] \"{1}\" profile selected. Updating \"{2}\" monitor brightness to {3}...", DateTime.Now, profile.Key, monitor.Key, anotherBrightness);

            monitor.MonitorInstance.Brightness = anotherBrightness;
        }

        Console.WriteLine("{0:O} [OnSync] Finished!", DateTime.Now);
    }
}