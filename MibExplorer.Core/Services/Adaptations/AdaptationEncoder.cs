using System.Globalization;
using MibExplorer.Models.Adaptations;

namespace MibExplorer.Services.Adaptations;

/// <summary>Write-side planning: turns a chosen label into the new raw value of the physical key, merged into the value that was just read from the unit. Pure logic, nothing is sent.</summary>
public static class AdaptationEncoder
{
    public sealed record Result(bool Ok, string NewRaw, string Error);

    public static string FormatHex(byte[] b) => string.Join(" ", b.Select(x => x.ToString("X2", CultureInfo.InvariantCulture)));

    public static Result Plan(AdaptationCatalogItem item, string currentRaw, string chosenLabel)
    {
        if (!item.Ui.Editable) return Fail("This adaptation is read only in the catalog.");
        if (!item.Storage.Mapped) return Fail("This adaptation has no storage mapping.");
        if (string.IsNullOrWhiteSpace(currentRaw)) return Fail("The current value was not read from the unit. Read the group first.");
        string mode = item.Storage.Mode ?? "";
        var edit = item.Ui.Values.Where(v => !string.Equals(v.Label, "Reset selection", StringComparison.OrdinalIgnoreCase)).ToList();
        var chosen = edit.FirstOrDefault(v => string.Equals(v.Label, chosenLabel, StringComparison.OrdinalIgnoreCase));
        if (chosen is null && mode is not "scalar") return Fail("'" + chosenLabel + "' is not an allowed value for this adaptation.");

        if (mode.Equals("packedFlags", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(currentRaw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cur) || item.Storage.Mask is not int mask) return Fail("The stored value cannot be parsed as a number.");
            bool on = IsOn(chosen!.Label);
            if (!on && !IsOff(chosen.Label)) return Fail("Cannot tell whether '" + chosen.Label + "' means on or off.");
            int n = on ? cur | mask : cur & ~mask;
            return new(true, n.ToString(CultureInfo.InvariantCulture), "");
        }
        if (mode.Equals("blobBit", StringComparison.OrdinalIgnoreCase))
        {
            var b = AdaptationDecoder.ParseHex(currentRaw);
            if (b is null || item.Storage.ByteIndex is not int bi || item.Storage.BitIndex is not int bit || bi < 0 || bi >= b.Length || bit < 0 || bit > 7) return Fail("The stored blob does not contain this bit.");
            bool on = IsOn(chosen!.Label);
            if (!on && !IsOff(chosen.Label)) return Fail("Cannot tell whether '" + chosen.Label + "' means on or off.");
            b[bi] = on ? (byte)(b[bi] | (1 << bit)) : (byte)(b[bi] & ~(1 << bit));
            return new(true, FormatHex(b), "");
        }
        if (mode.Equals("blobBitsEnum", StringComparison.OrdinalIgnoreCase))
        {
            var b = AdaptationDecoder.ParseHex(currentRaw);
            if (b is null || item.Storage.ByteIndex is not int bi || item.Storage.BitIndex is not int bit || item.Storage.BitWidth is not int w) return Fail("The stored blob cannot be parsed.");
            var so = item.Storage.Values.FirstOrDefault(v => string.Equals(v.Label, chosen!.Label, StringComparison.OrdinalIgnoreCase) || string.Equals(v.Raw, chosen.Raw, StringComparison.OrdinalIgnoreCase));
            string rv = so is null ? "" : (string.IsNullOrWhiteSpace(so.Raw) ? so.Label : so.Raw!);
            if (!int.TryParse(rv, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val)) return Fail("No storage value is defined for '" + chosen!.Label + "'.");
            if (!TrySetBitRange(b, bi, bit, w, val)) return Fail("The value does not fit the stored bit range.");
            return new(true, FormatHex(b), "");
        }
        if (mode.Equals("scalarEnum", StringComparison.OrdinalIgnoreCase))
        {
            var so = item.Storage.Values.FirstOrDefault(v => string.Equals(v.Label, chosen!.Label, StringComparison.OrdinalIgnoreCase));
            string rv = so is null ? "" : (string.IsNullOrWhiteSpace(so.Raw) ? so.Label : so.Raw!);
            return string.IsNullOrWhiteSpace(rv) ? Fail("No storage value is defined for '" + chosen!.Label + "'.") : new(true, rv, "");
        }
        return Fail("Storage mode '" + mode + "' is not editable in this app yet.");
    }

    static Result Fail(string e) => new(false, "", e);
    static bool IsOn(string l) => new[] { "activated", "on", "available", "available.", "enabled", "[VN]_activated", "[VN]_on" }.Any(x => x.Equals(l, StringComparison.OrdinalIgnoreCase));
    static bool IsOff(string l) => new[] { "not activated", "[VN]_not_activated", "not active", "[VN]_not_active", "off", "[VN]_off", "not available", "[VN]_not_available", "disabled", "locked" }.Any(x => x.Equals(l, StringComparison.OrdinalIgnoreCase));

    public static bool TrySetBitRange(byte[] bytes, int byteIndex, int bitIndex, int bitWidth, int value)
    {
        if (byteIndex < 0 || bitIndex < 0 || bitIndex > 7 || bitWidth <= 0 || bitWidth > 31) return false;
        if (value < 0 || value > (1 << bitWidth) - 1) return false;
        var copy = (byte[])bytes.Clone();
        for (int i = 0; i < bitWidth; i++)
        {
            int abs = bitIndex + i, cb = byteIndex + abs / 8, cbit = abs % 8;
            if (cb < 0 || cb >= copy.Length) return false;
            byte m = (byte)(1 << cbit);
            copy[cb] = (value & (1 << i)) != 0 ? (byte)(copy[cb] | m) : (byte)(copy[cb] & ~m);
        }
        Array.Copy(copy, bytes, bytes.Length);
        return true;
    }

    /// <summary>One transaction per physical key, started from the raw value just read. Nothing is written by building it.</summary>
    public static PhysicalWriteTransaction Transaction(PhysicalStorageKey key, string originalRaw, string mergedRaw) =>
        new() { PhysicalKey = key, OriginalRawValue = originalRaw.Trim(), MergedRawValue = mergedRaw.Trim() };
}
