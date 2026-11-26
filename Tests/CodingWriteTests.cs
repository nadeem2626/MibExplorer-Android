using MibExplorer.Models.Coding;
using MibExplorer.Services.Coding;

public static class CodingWriteTests
{
    static int pass, fail;
    static void Check(string n, bool ok) { if (ok) { pass++; Console.WriteLine("PASS " + n); } else { fail++; Console.WriteLine("FAIL " + n); } }
    const string Hex = "04738001FF00000011110001000E04009F0000F0017000001E"; // a 25 byte blob (from a user photo)

    public static async Task<int> RunAsync()
    {
        var svc = new CodingFeatureService(); await svc.LoadAsync("");
        var orig = CodingEditing.FromHex(Hex);
        Check("blob is 25 bytes", orig.Length == 25);
        // selection: choose Brand = VW (raw 2): only byte 0 changes, one pending change
        var cur = (byte[])orig.Clone();
        var brand = svc.Decode(CodingEditing.ToBytes(orig)).First(f => f.Id == "brand");
        Check("brand reads Seat (4)", brand.RawValue == "4");
        Check("SetField accepts a catalog option", CodingEditing.SetField(cur, brand.Byte, brand.Mask, brand.Shift, 2));
        var diffs = Enumerable.Range(0, 25).Where(i => cur[i] != orig[i]).ToList();
        Check("only byte 0 differs after choosing VW", diffs.Count == 1 && diffs[0] == 0 && cur[0] == 0x02);
        var ch = CodingEditing.Changes(svc, orig, cur);
        Check("exactly one pending change, brand 4 > 2", ch.Count == 1 && ch[0].FeatureId == "brand" && ch[0].NewRawValue == "2");
        Check("a value that does not fit is refused", !CodingEditing.SetField(cur, brand.Byte, brand.Mask, brand.Shift, 99));
        Check("choosing the current value changes nothing", CodingEditing.Changes(svc, orig, (byte[])orig.Clone()).Count == 0);
        string target = CodingEditing.ToHex(cur);

        // History as upstream: keyed by the VIN string as read. UNKNOWN is a shared folder (documented caveat).
        string root = Path.Combine(Path.GetTempPath(), "cw-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        CodingHistoryService.RootOverride = root; var h = new CodingHistoryService();
        await h.AddSnapshotAsync("UNKNOWN", Hex, 25, "car1", new List<CodingPendingChange>());
        await h.AddSnapshotAsync("UNKNOWN", target, 25, "car2", new List<CodingPendingChange>());
        await h.AddSnapshotAsync("WAUZZZ4M1MD010451", Hex, 25, "vin", new List<CodingPendingChange>());
        Check("KNOWN CAVEAT: two cars with VIN UNKNOWN share one history (as upstream)", (await h.LoadSnapshotsAsync("UNKNOWN")).Count == 2);
        Check("a real VIN has its own history", (await h.LoadSnapshotsAsync("WAUZZZ4M1MD010451")).Count == 1 && (await h.LoadSnapshotsAsync("OTHERVIN")).Count == 0);

        // write flow
        int writes = 0, snaps = 0;
        Func<Task<string>> unit = () => Task.FromResult(Hex);
        Func<Task> snapOk = () => { snaps++; return Task.CompletedTask; };
        Func<string, Task<CodingWriteResult>> wOk = t => { writes++; return Task.FromResult(new CodingWriteResult { Success = true, BeforeHex = Hex, AfterHex = t, Message = "ok" }); };
        var o = await CodingWriteFlow.RunAsync(null, Hex, target, unit, snapOk, wOk);
        Check("no unit id: nothing written, no snapshot", !o.Written && writes == 0 && snaps == 0);
        o = await CodingWriteFlow.RunAsync("UNKNOWN", Hex, Hex, unit, snapOk, wOk);
        Check("zero change: nothing written", !o.Written && writes == 0);
        o = await CodingWriteFlow.RunAsync("UNKNOWN", Hex, target, () => Task.FromResult(target), snapOk, wOk);
        Check("changed since read: nothing written", !o.Written && writes == 0 && o.Message.Contains("changed since"));
        o = await CodingWriteFlow.RunAsync("UNKNOWN", Hex, target, () => throw new IOException("link down"), snapOk, wOk);
        Check("cannot re-read: nothing written", !o.Written && writes == 0);
        o = await CodingWriteFlow.RunAsync("UNKNOWN", Hex, target, unit, () => throw new IOException("disk full"), wOk);
        Check("snapshot fails: the write is never called", !o.Written && writes == 0 && o.Message.Contains("snapshot"));
        o = await CodingWriteFlow.RunAsync("UNKNOWN", Hex, target, unit, snapOk, wOk);
        Check("success: snapshot first, one write, verified read-back", o.Written && o.VerifiedReadback && writes == 1 && snaps == 1 && o.AfterHex == target);
        o = await CodingWriteFlow.RunAsync("UNKNOWN", Hex, target, unit, snapOk, t => Task.FromResult(new CodingWriteResult { Success = true, BeforeHex = Hex, AfterHex = Hex, Message = "x" }));
        Check("success claimed but read-back differs: reported as mismatch", o.Written && !o.VerifiedReadback && o.Message.Contains("READ-BACK DID NOT MATCH"));
        o = await CodingWriteFlow.RunAsync("UNKNOWN", Hex, target, unit, snapOk, t => Task.FromResult(new CodingWriteResult { Success = false, BeforeHex = Hex, AfterHex = "", Message = "pc failed" }));
        Check("write failed: reported, not verified", o.Written && !o.VerifiedReadback);
        o = await CodingWriteFlow.RunAsync("UNKNOWN", Hex, target, unit, snapOk, t => throw new IOException("lost"));
        Check("write throws: tells him to read again", o.Written && !o.VerifiedReadback && o.Message.Contains("Read the coding again"));
        Directory.Delete(root, true);
        Console.WriteLine($"{pass} passed, {fail} failed"); return fail == 0 ? 0 : 1;
    }
}
