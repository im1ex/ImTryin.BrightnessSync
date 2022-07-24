using System;
using System.Management;
using System.Text;

namespace BrightnessSync.Extensions;

public static class PropertyDataExtensions
{
    public static string ReadStringFromUInt16ArrayValue(this PropertyData propertyData)
    {
        var chars = (ushort[]) propertyData.Value;

        var stringBuilder = new StringBuilder(chars.Length);

        for (int i = 0; i < chars.Length && chars[i] != 0; i++)
            stringBuilder.Append((char) chars[i]);

        return stringBuilder.ToString();
    }

    public static DateTime ReadDateTimeFromUInt64(this PropertyData propertyData)
    {
        return DateTime.FromFileTime((long) (ulong) propertyData.Value);
    }
}