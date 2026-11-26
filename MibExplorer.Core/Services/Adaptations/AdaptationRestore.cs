using MibExplorer.Models.Adaptations;

namespace MibExplorer.Services.Adaptations;

/// <summary>Restore from History: put the saved "before" value of a physical key back. Pure planning; the caller reads the live value, previews, and runs the usual gates.</summary>
public static class AdaptationRestore
{
    public static PhysicalStorageKey? ParseKey(string text)
    {
        var p = (text ?? "").Split(':');
        if (p.Length != 3 || p.Any(x => string.IsNullOrWhiteSpace(x) || x.Any(char.IsControl) || x.Contains(';') || x.Contains(' '))) return null;
        return new PhysicalStorageKey(p[0].Trim(), p[1].Trim(), p[2].Trim());
    }

    /// <summary>Returns the transaction (original = value read now, merged = saved before value) or an error. No change needed when they already match.</summary>
    public static (PhysicalWriteTransaction? Tx, string Error) Plan(AdaptationHistoryEntry entry, string liveRaw)
    {
        var key = ParseKey(entry.PhysicalKey);
        if (key is null) return (null, "The saved key '" + entry.PhysicalKey + "' is not in a form that can be restored.");
        if (string.IsNullOrWhiteSpace(entry.OriginalRawValue)) return (null, "This history entry has no saved before value.");
        if (string.IsNullOrWhiteSpace(liveRaw)) return (null, "The current value could not be read from the unit.");
        if (entry.OriginalRawValue.Any(c => char.IsControl(c) || c == ';')) return (null, "The saved value contains characters that are not allowed.");
        var tx = new PhysicalWriteTransaction { PhysicalKey = key, OriginalRawValue = liveRaw.Trim(), MergedRawValue = entry.OriginalRawValue.Trim() };
        if (!tx.IsDirty) return (null, "The unit already holds the saved value. Nothing to restore.");
        return (tx, "");
    }
}
