using MibExplorer.Models.Coding;

namespace MibExplorer.Services.Coding;

/// <summary>Pure helpers for editing a 5F coding blob (no I/O), used by the Android Coding screen and the tests.</summary>
public static class CodingEditing
{
    public static byte[] FromHex(string hex)
    {
        hex = (hex ?? "").Trim();
        if (hex.Length % 2 != 0) throw new FormatException("Odd number of hex digits.");
        var b = new byte[hex.Length / 2];
        for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return b;
    }

    public static string ToHex(byte[] bytes) => string.Concat(bytes.Select(x => x.ToString("X2")));

    public static List<CodingByte> ToBytes(byte[] v) => v.Select((x, i) => new CodingByte { Index = i, Value = x }).ToList();

    /// <summary>Sets one field (mask/shift inside byte) to a raw numeric value. Returns false if out of range or value does not fit the mask.</summary>
    public static bool SetField(byte[] v, int byteIndex, int mask, int shift, int raw)
    {
        if (byteIndex < 0 || byteIndex >= v.Length) return false;
        mask &= 0xFF;
        int shifted = (raw << shift) & 0xFF;
        if ((shifted & ~mask) != 0 || ((raw << shift) >> 8) != 0) return false;
        v[byteIndex] = (byte)((v[byteIndex] & ~mask) | shifted);
        return true;
    }

    public static bool SetBit(byte[] v, int byteIndex, int bit, bool on)
    {
        if (byteIndex < 0 || byteIndex >= v.Length || bit < 0 || bit > 7) return false;
        v[byteIndex] = (byte)(on ? v[byteIndex] | (1 << bit) : v[byteIndex] & ~(1 << bit));
        return true;
    }

    /// <summary>Features whose decoded raw value differs between two blobs.</summary>
    public static List<CodingPendingChange> Changes(CodingFeatureService svc, byte[] before, byte[] after)
    {
        var a = svc.Decode(ToBytes(before)).ToDictionary(f => f.Id);
        var res = new List<CodingPendingChange>();
        foreach (var f in svc.Decode(ToBytes(after)))
        {
            if (!a.TryGetValue(f.Id, out var o) || o.RawValue == f.RawValue) continue;
            res.Add(new CodingPendingChange
            {
                FeatureId = f.Id, Label = f.Label, CurrentValue = o.Value, CurrentRawValue = o.RawValue,
                NewValue = f.Value, NewRawValue = f.RawValue, Key = f.Key, Type = f.Type, Byte = f.Byte,
                CurrentByteHex = f.Byte < before.Length ? before[f.Byte].ToString("X2") : "",
                NewByteHex = f.Byte < after.Length ? after[f.Byte].ToString("X2") : ""
            });
        }
        return res;
    }
}
