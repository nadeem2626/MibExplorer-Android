using System.Globalization;
using System.Text.RegularExpressions;
using MibExplorer.Models.Adaptations;

namespace MibExplorer.Services.Adaptations;

/// <summary>Read-side decode of a stored raw value into the catalog label. Ported from the upstream view model logic (same rules, no UI).</summary>
public static class AdaptationDecoder
{
    public static string Decode(AdaptationCatalogItem item, string raw)
    {
        var edit = Options(item.Ui.Values); var stor = Options(item.Storage.Values);
        if (!item.Storage.Mapped) return Catalog(edit, raw);
        string mode = item.Storage.Mode ?? "";
        if (Is(mode, "scalar")) return Catalog(edit, raw);
        if (Is(mode, "scalarEnum")) return Storage(edit, stor, raw);
        if (Is(mode, "packedFlags"))
        {
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pv) || item.Storage.Mask is not int mask) return Catalog(edit, raw);
            return Bool(edit, (pv & mask) != 0);
        }
        if (Is(mode, "blobBit"))
        {
            if (item.Storage.ByteIndex is not int bi || item.Storage.BitIndex is not int bit) return Catalog(edit, raw);
            var b = ParseHex(raw);
            if (b is null || bi < 0 || bi >= b.Length || bit < 0 || bit > 7) return Catalog(edit, raw);
            return Bool(edit, (b[bi] & (1 << bit)) != 0);
        }
        if (Is(mode, "blobBitsEnum"))
        {
            if (item.Storage.ByteIndex is not int bi || item.Storage.BitIndex is not int bit || item.Storage.BitWidth is not int w) return Catalog(edit, raw);
            var b = ParseHex(raw);
            if (b is null || !TryExtractBitRange(b, bi, bit, w, out int v)) return Catalog(edit, raw);
            return Storage(edit, stor, v.ToString(CultureInfo.InvariantCulture));
        }
        return Catalog(edit, raw);
    }

    static bool Is(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
    static List<(string Value, string Label)> Options(List<AdaptationCatalogEnumValue> vals) =>
        vals.Where(v => !string.Equals(v.Label, "Reset selection", StringComparison.OrdinalIgnoreCase))
            .Select(v => (string.IsNullOrWhiteSpace(v.Raw) ? v.Label : v.Raw!, v.Label)).ToList();
    static string Catalog(List<(string Value, string Label)> o, string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "-";
        foreach (var x in o) if (string.Equals(x.Value, v, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Label, v, StringComparison.OrdinalIgnoreCase)) return x.Label;
        return v;
    }
    static string Storage(List<(string Value, string Label)> edit, List<(string Value, string Label)> s, string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "-";
        foreach (var x in s) if (string.Equals(x.Value, v, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Label, v, StringComparison.OrdinalIgnoreCase)) return x.Label;
        return Catalog(edit, v);
    }
    static string Bool(List<(string Value, string Label)> edit, bool active)
    {
        foreach (var o in edit) { if (active && Enabled(o.Label)) return o.Label; if (!active && Disabled(o.Label)) return o.Label; }
        return active ? "activated" : "not activated";
    }
    static readonly string[] En = { "activated", "on", "available", "available.", "enabled", "[VN]_activated", "[VN]_on" };
    static readonly string[] Dis = { "not activated", "[VN]_not_activated", "not active", "[VN]_not_active", "off", "[VN]_off", "not available", "[VN]_not_available", "disabled", "locked" };
    static bool Enabled(string? v) => !string.IsNullOrWhiteSpace(v) && En.Any(x => x.Equals(v, StringComparison.OrdinalIgnoreCase));
    static bool Disabled(string? v) => !string.IsNullOrWhiteSpace(v) && Dis.Any(x => x.Equals(v, StringComparison.OrdinalIgnoreCase));

    public static bool TryExtractBitRange(byte[] bytes, int byteIndex, int bitIndex, int bitWidth, out int value)
    {
        value = 0;
        if (byteIndex < 0 || bitIndex < 0 || bitIndex > 7 || bitWidth <= 0 || bitWidth > 31) return false;
        for (int i = 0; i < bitWidth; i++)
        {
            int abs = bitIndex + i, cb = byteIndex + abs / 8, cbit = abs % 8;
            if (cb < 0 || cb >= bytes.Length) return false;
            if ((bytes[cb] & (1 << cbit)) != 0) value |= 1 << i;
        }
        return true;
    }

    public static byte[]? ParseHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var bytes = new List<byte>();
        foreach (string rawLine in value.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string line = Regex.Replace(rawLine.Trim(), @"^[0-9A-Fa-f]{8}\s*:\s*", "");
            int pipe = line.IndexOf('|'); if (pipe >= 0) line = line[..pipe];
            foreach (string token in line.Split(new[] { ' ', '\t', ',', ';', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string t = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token[2..] : token;
                if (t.Length != 2 || !t.All(Uri.IsHexDigit)) continue;
                bytes.Add(byte.Parse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
        }
        if (bytes.Count > 0) return bytes.ToArray();
        string compact = Regex.Replace(value, @"[^0-9A-Fa-f]", "");
        if (compact.Length == 0 || compact.Length % 2 != 0) return null;
        for (int i = 0; i < compact.Length; i += 2) bytes.Add(byte.Parse(compact.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return bytes.ToArray();
    }
}
