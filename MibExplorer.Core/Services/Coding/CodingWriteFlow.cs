using MibExplorer.Models.Coding;

namespace MibExplorer.Services.Coding;

public sealed record CodingWriteOutcome(bool Written, bool VerifiedReadback, string Message, string AfterHex);

/// <summary>The order of a coding write, with the unit access passed in so it can be tested without a unit.
/// Refuses (writes nothing) when: no unit id, nothing changed, the unit's coding differs from what the user read,
/// or the pre-write snapshot cannot be saved. After the write it trusts only a read-back equal to the target.</summary>
public static class CodingWriteFlow
{
    public static async Task<CodingWriteOutcome> RunAsync(
        string? unitId, string originalHex, string targetHex,
        Func<Task<string>> readUnitHex,
        Func<Task> saveSnapshotOfOriginal,
        Func<string, Task<CodingWriteResult>> writeToUnit)
    {
        if (unitId is null) return new(false, false, "Nothing was written: there is no unit id to file the recovery snapshot under.", "");
        if (string.Equals(originalHex, targetHex, StringComparison.OrdinalIgnoreCase)) return new(false, false, "Nothing to write: no change.", "");
        string now;
        try { now = (await readUnitHex()).Trim(); }
        catch (Exception ex) { return new(false, false, "Nothing was written: could not re-read the coding: " + ex.Message, ""); }
        if (!string.Equals(now, originalHex.Trim(), StringComparison.OrdinalIgnoreCase)) return new(false, false, "Nothing was written: the coding on the unit changed since you read it. Read it again.", "");
        try { await saveSnapshotOfOriginal(); }
        catch (Exception ex) { return new(false, false, "Nothing was written: the snapshot could not be saved first: " + ex.Message, ""); }
        CodingWriteResult r;
        try { r = await writeToUnit(targetHex); }
        catch (Exception ex) { return new(true, false, "The write may have partly run and its result could not be read back: " + ex.Message + " Read the coding again before doing anything else.", ""); }
        bool match = string.Equals((r.AfterHex ?? "").Trim(), targetHex.Trim(), StringComparison.OrdinalIgnoreCase);
        if (r.Success && match) return new(true, true, "Write reported success and the read-back equals what was written. " + r.Message, r.AfterHex!);
        return new(true, false, "READ-BACK DID NOT MATCH or the write failed. The unit may now hold different coding than intended. Before: " + r.BeforeHex + "  After: " + r.AfterHex + ". " + r.Message + " Restore from the snapshot only after reading the coding again.", r.AfterHex ?? "");
    }
}
