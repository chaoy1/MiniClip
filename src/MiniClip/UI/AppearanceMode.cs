using Microsoft.Win32;

namespace MiniClip.UI;

/// <summary>The user's saved appearance choice, distinct from the active light/dark palette.</summary>
public enum AppearanceMode
{
    Light,
    Dark,
    System,
}

public static class AppearanceModeExtensions
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string NightLightStateKey =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";
    private const string NightLightSettingsKey =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.settings\windows.data.bluelightreduction.settings";

    public static AppearanceMode Parse(string? value) =>
        Enum.TryParse<AppearanceMode>(value, ignoreCase: true, out var mode)
            ? mode
            : AppearanceMode.Dark;

    public static PopupTheme Resolve(AppearanceMode mode, bool systemIsLight) => mode switch
    {
        AppearanceMode.Light => PopupTheme.Light,
        AppearanceMode.Dark => PopupTheme.Dark,
        _ => systemIsLight ? PopupTheme.Light : PopupTheme.Dark,
    };

    public static bool SystemIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            var appsLight = key?.GetValue("AppsUseLightTheme") is not int apps || apps != 0;
            var windowsLight = key?.GetValue("SystemUsesLightTheme") is not int windows || windows != 0;
            return IsSystemLight(appsLight, windowsLight, NightLightIsOn());
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    private static bool IsSystemLight(bool appsLight, bool windowsLight, bool nightLight) =>
        appsLight && windowsLight && !nightLight;

    private static bool NightLightIsOn()
    {
        try
        {
            using var stateKey = Registry.CurrentUser.OpenSubKey(NightLightStateKey);
            if (stateKey?.GetValue("Data") is byte[] state && ParseNightLightState(state) == true)
                return true;

            using var settingsKey = Registry.CurrentUser.OpenSubKey(NightLightSettingsKey);
            var now = DateTime.Now;
            return settingsKey?.GetValue("Data") is byte[] settings
                   && ParseNightLightSchedule(settings, now.Hour * 60 + now.Minute) == true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads only the enabled flag from Windows' private CloudStore value. Unknown
    /// formats return null so an OS update cannot turn arbitrary bytes into a theme.
    /// </summary>
    private static bool? ParseNightLightState(byte[] data)
    {
        if (!TryExtractNightLightPayload(data, out var payload))
            return null;
        if (payload.Length < 9 || !payload[..4].SequenceEqual(new byte[] { 0x43, 0x42, 0x01, 0x00 }))
            return null;

        var enabled = payload[4] == 0x10;
        var field = enabled ? 6 : 4;
        if (enabled && payload[5] != 0x00)
            return null;
        if (payload.Length < field + 5 || !payload.Slice(field, 5).SequenceEqual(new byte[] { 0xD0, 0x0A, 0x02, 0xC6, 0x14 }))
            return null;
        return enabled;
    }

    private static bool? ParseNightLightSchedule(byte[] data, int minuteOfDay)
    {
        if (minuteOfDay is < 0 or >= 1440 || !TryExtractNightLightPayload(data, out var payload)
            || payload.Length < 5 || !payload[..4].SequenceEqual(new byte[] { 0x43, 0x42, 0x01, 0x00 }))
            return null;

        var index = 4;
        if (payload[index] != 0x02)
            return false; // Scheduling is disabled.
        if (!Consume(payload, ref index, new byte[] { 0x02, 0x01 }))
            return null;

        var manualHours = payload.Length - index >= 2 && payload[index] == 0xC2 && payload[index + 1] == 0x0A;
        if (manualHours && !Consume(payload, ref index, new byte[] { 0xC2, 0x0A, 0x00 }))
            return null;

        if (!TryReadTimeBlock(payload, ref index, new byte[] { 0xCA, 0x14 }, out var start)
            || !TryReadTimeBlock(payload, ref index, new byte[] { 0xCA, 0x1E }, out var end))
            return null;

        if (payload.Length - index >= 2 && payload[index] == 0xCF && payload[index + 1] == 0x28)
        {
            index += 2;
            if (!TryReadVarUInt(payload, ref index, out _))
                return null;
        }

        if (!TryReadTimeBlock(payload, ref index, new byte[] { 0xCA, 0x32 }, out var sunset)
            || !TryReadTimeBlock(payload, ref index, new byte[] { 0xCA, 0x3C }, out var sunrise))
            return null;

        var from = manualHours ? start : sunset;
        var to = manualHours ? end : sunrise;
        if (from == to)
            return null;
        return from < to
            ? minuteOfDay >= from && minuteOfDay < to
            : minuteOfDay >= from || minuteOfDay < to;
    }

    private static bool TryReadTimeBlock(ReadOnlySpan<byte> data, ref int index,
        ReadOnlySpan<byte> fieldHeader, out int minuteOfDay)
    {
        minuteOfDay = 0;
        if (!Consume(data, ref index, fieldHeader))
            return false;

        var hour = 0;
        var minute = 0;
        while (index < data.Length && data[index] != 0x00)
        {
            var field = data[index++];
            if (index >= data.Length)
                return false;
            if (field == 0x0E)
                hour = data[index++];
            else if (field == 0x2E)
                minute = data[index++];
            else
                return false;
        }
        if (index >= data.Length || hour > 23 || minute > 59)
            return false;
        index++; // End of TimeBlock.
        minuteOfDay = hour * 60 + minute;
        return true;
    }

    private static bool TryExtractNightLightPayload(byte[] data, out ReadOnlySpan<byte> payload)
    {
        payload = default;
        ReadOnlySpan<byte> bytes = data;
        if (bytes.Length < 31 || !bytes[..4].SequenceEqual(new byte[] { 0x43, 0x42, 0x01, 0x00 }))
            return false;

        var index = 4;
        if (!Consume(bytes, ref index, new byte[] { 0x0A, 0x02, 0x01, 0x00, 0x2A, 0x06 })
            || !TryReadVarUInt(bytes, ref index, out _)
            || !Consume(bytes, ref index, new byte[] { 0x2A, 0x2B, 0x0E })
            || !TryReadVarUInt(bytes, ref index, out var count)
            || bytes.Length - index < 3
            || count > (ulong)(bytes.Length - index - 3))
            return false;

        payload = bytes.Slice(index, (int)count);
        return true;
    }

    private static bool Consume(ReadOnlySpan<byte> data, ref int index, ReadOnlySpan<byte> expected)
    {
        if (data.Length - index < expected.Length || !data.Slice(index, expected.Length).SequenceEqual(expected))
            return false;
        index += expected.Length;
        return true;
    }

    private static bool TryReadVarUInt(ReadOnlySpan<byte> data, ref int index, out ulong value)
    {
        value = 0;
        for (var bit = 0; bit <= 63 && index < data.Length; bit += 7)
        {
            var next = data[index++];
            if (bit == 63 && next > 1)
                return false;
            value |= (ulong)(next & 0x7F) << bit;
            if ((next & 0x80) == 0)
                return true;
        }
        return false;
    }
}
