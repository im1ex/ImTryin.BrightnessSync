﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ImTryin.BrightnessSync.Api;

internal static class MonitorApi
{
    public const uint D3DKMDT_VOT_INTERNAL = 0x80000000u;

    #region EnumDisplayDevices

    private readonly struct DisplayDevice
    {
        public readonly int StructureSize;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public readonly string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public readonly string DeviceString;

        public readonly uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public readonly string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public readonly string DeviceKey;

        public DisplayDevice()
        {
            StructureSize = Marshal.SizeOf<DisplayDevice>();
            DeviceName = string.Empty;
            DeviceString = string.Empty;
            StateFlags = 0;
            DeviceId = string.Empty;
            DeviceKey = string.Empty;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayDevices(string? deviceName, int deviceIndex, ref DisplayDevice displayDevice, uint flags);

    #endregion

    #region GetPhysicalMonitors

    [StructLayout(LayoutKind.Sequential)]
    private class UnicodeString
    {
        public readonly ushort Length;
        public readonly ushort MaximumLength;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string Buffer;

        public UnicodeString(string s)
        {
            if ((ushort.MaxValue - 1) >> 1 < s.Length)
                throw new ArgumentOutOfRangeException(nameof(s));

            Length = (ushort)(s.Length << 1);
            MaximumLength = (ushort)(Length + 2);
            Buffer = s;
        }
    }

    [DllImport("Gdi32.dll")]
    private static extern int GetNumberOfPhysicalMonitors(UnicodeString deviceName, out int numberOfPhysicalMonitors);

    [DllImport("Gdi32.dll")]
    private static extern int GetPhysicalMonitors(
        UnicodeString deviceName,
        int physicalMonitorArraySize,
        out int numPhysicalMonitorHandlesInArray,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)]
        IntPtr[] physicalMonitorArray);

    [DllImport("Gdi32.dll")]
    public static extern int DestroyPhysicalMonitorInternal(IntPtr physicalMonitorHandle);

    #endregion

    #region CapabilitiesRequestAndCapabilitiesReply

    [DllImport("Dxva2.dll", SetLastError = true)]
    private static extern bool GetCapabilitiesStringLength(IntPtr physicalMonitorHandle, out int capabilitiesStringLengthInCharacters);

    [DllImport("Dxva2.dll", SetLastError = true)]
    private static extern bool CapabilitiesRequestAndCapabilitiesReply(
        IntPtr hPhysicalMonitor,
        [MarshalAs(UnmanagedType.LPStr, SizeParamIndex = 2)]
        StringBuilder asciiCapabilitiesStringBuilder,
        int capabilitiesStringLengthInCharacters);

    #endregion

    private static readonly ConcurrentDictionary<string, string> __deviceCapabilitiesStringCache = new();

    public static void ClearDeviceCapabilitiesStringCache()
    {
        __deviceCapabilitiesStringCache.Clear();
    }

    public static IEnumerable<PhysicalMonitorDevice> GetPhysicalMonitorDevices()
    {
        var displayDevice = new DisplayDevice();
        for (int i = 0; EnumDisplayDevices(null, i, ref displayDevice, 0); i++)
        {
            var deviceName = displayDevice.DeviceName;
            var deviceNameUnicodeString = new UnicodeString(deviceName);

            var ntStatus = GetNumberOfPhysicalMonitors(deviceNameUnicodeString, out var numberOfPhysicalMonitors);
            if (0 != ntStatus)
                continue;

            var physicalMonitorHandles = new IntPtr[numberOfPhysicalMonitors];
            ntStatus = GetPhysicalMonitors(deviceNameUnicodeString, numberOfPhysicalMonitors, out var numPhysicalMonitorHandlesInArray, physicalMonitorHandles);
            if (0 != ntStatus || numberOfPhysicalMonitors != numPhysicalMonitorHandlesInArray)
                continue;

            for (int j = 0; EnumDisplayDevices(deviceName, j, ref displayDevice, 1) && j < numberOfPhysicalMonitors; j++)
            {
                var deviceId = displayDevice.DeviceId;
                var physicalMonitorHandle = physicalMonitorHandles[j];

                var capabilitiesString = __deviceCapabilitiesStringCache.GetOrAdd(deviceId, _ =>
                {
                    if (!GetCapabilitiesStringLength(physicalMonitorHandle, out var capabilitiesStringLengthInCharacters))
                        return string.Empty;

                    var asciiCapabilitiesStringBuilder = new StringBuilder(capabilitiesStringLengthInCharacters);

                    if (!CapabilitiesRequestAndCapabilitiesReply(physicalMonitorHandle, asciiCapabilitiesStringBuilder,
                            asciiCapabilitiesStringBuilder.Capacity))
                        return string.Empty;

                    return asciiCapabilitiesStringBuilder.ToString();
                });

                yield return new PhysicalMonitorDevice(deviceId, physicalMonitorHandle, capabilitiesString);
            }
        }
    }


    [DllImport("Dxva2.dll", SetLastError = true)]
    public static extern bool GetMonitorBrightness(IntPtr physicalMonitorHandle, out int minimum, out int current, out int maximum);

    [DllImport("Dxva2.dll", SetLastError = true)]
    public static extern bool SetMonitorBrightness(IntPtr physicalMonitorHandle, int value);

    private const int RetryIntervalMs = 100;
    private const int MaxRetries = 10;

    public static (int Minimum, int Current, int Maximum) GetMonitorBrightnessWithRetries(IntPtr physicalMonitorHandle,
        int retryIntervalMs = RetryIntervalMs, int maxRetries = MaxRetries)
    {
        int minimum, current, maximum, retries = 0;
        while (!GetMonitorBrightness(physicalMonitorHandle, out minimum, out current, out maximum))
        {
            if (++retries >= maxRetries)
                throw new Exception();
            Thread.Sleep(retryIntervalMs);
        }
        return (minimum, current, maximum);
    }

    public static void SetMonitorBrightnessWithRetries(IntPtr physicalMonitorHandle, int value,
        int retryIntervalMs = RetryIntervalMs, int maxRetries = MaxRetries)
    {
        var retries = 0;
        while (!SetMonitorBrightness(physicalMonitorHandle, value))
        {
            if (++retries >= maxRetries)
                throw new Exception();
            Thread.Sleep(retryIntervalMs);
        }
    }
}