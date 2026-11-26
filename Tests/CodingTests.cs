using MibExplorer.Models.Coding;
using MibExplorer.Services.Coding;

public static class CodingTests
{
    static int pass, fail;
    static void Check(string n, bool ok) { if (ok) { pass++; Console.WriteLine("PASS " + n); } else { fail++; Console.WriteLine("FAIL " + n); } }

    public static async Task<int> RunAsync()
    {
        var svc = new CodingFeatureService();
        await svc.LoadAsync("");
        // catalog is embedded; build a blob of the usual length
        var blob = new byte[0x65 * 1 + 0x4000 / 0x100]; // placeholder size, overwritten below
        int max = 0;
        var probe = svc.Decode(CodingEditing.ToBytes(new byte[300]));
        Check("catalog decodes 124 features", probe.Count == 124);
        max = probe.Max(f => f.Byte);
        var zero = new byte[max + 1];
        var feats = svc.Decode(CodingEditing.ToBytes(zero));
        Check("decode of zero blob yields a value text for every feature", feats.All(f => !string.IsNullOrEmpty(f.Value)));

        // Round trip: for every feature and every option that fits, SetField then Decode returns that raw; other features unchanged
        int tried = 0, bad = 0, collide = 0;
        foreach (var f in feats)
        {
            foreach (var opt in f.Options)
            {
                if (!int.TryParse(opt.Raw, out int raw)) continue;
                var v = (byte[])zero.Clone();
                if (!CodingEditing.SetField(v, f.Byte, f.Mask, f.Shift, raw)) continue;
                tried++;
                var back = svc.Decode(CodingEditing.ToBytes(v)).First(x => x.Id == f.Id);
                if (back.RawValue != opt.Raw) bad++;
                var ch = CodingEditing.Changes(svc, zero, v);
                if (ch.Any(c => c.FeatureId != f.Id)) collide++;
            }
        }
        Check($"round trip over {tried} option settings", tried > 100 && bad == 0);
        Console.WriteLine($"  (overlapping-field side effects: {collide})");
        int agreeBad = 0, agreeN = 0;
        foreach (var f in svc.Decode(CodingEditing.ToBytes(zero)).Take(60))
        {
            var o = f.Options.FirstOrDefault(x => int.TryParse(x.Raw, out _)); if (o is null) continue;
            f.SelectedOption = o;
            var viaUpstream = CodingEditing.ToHex(svc.BuildModifiedBytes(CodingEditing.ToBytes(zero), new[] { f }).Select(b => b.Value).ToArray());
            var v = (byte[])zero.Clone(); CodingEditing.SetField(v, f.Byte, f.Mask, f.Shift, int.Parse(o.Raw));
            agreeN++; if (viaUpstream != CodingEditing.ToHex(v)) agreeBad++;
        }
        Check($"upstream BuildModifiedBytes agrees with SetField on {agreeN} features", agreeN > 20 && agreeBad == 0);
        // SetField refuses value that does not fit
        Check("SetField rejects too-wide value", !CodingEditing.SetField(new byte[2], 0, 0x03, 0, 7));
        Check("SetField rejects bad index", !CodingEditing.SetField(new byte[2], 5, 0xFF, 0, 1));
        var bits = new byte[1]; CodingEditing.SetBit(bits, 0, 7, true); CodingEditing.SetBit(bits, 0, 0, true);
        Check("SetBit 7 and 0 = 0x81", bits[0] == 0x81);
        CodingEditing.SetBit(bits, 0, 7, false);
        Check("clear bit 7 = 0x01", bits[0] == 0x01);
        Check("hex round trip", CodingEditing.ToHex(CodingEditing.FromHex("00ff10A5")) == "00FF10A5");
        Check("odd hex rejected", Throws(() => CodingEditing.FromHex("ABC")));

        // History
        string tmp = Path.Combine(Path.GetTempPath(), "mibx-hist-" + Guid.NewGuid().ToString("N"));
        CodingHistoryService.RootOverride = tmp;
        var h = new CodingHistoryService();
        var s1 = await h.AddSnapshotAsync("WVWZZZ1KZAW000001", "AABB", 2, "Manual", new List<CodingPendingChange>());
        var s2 = await h.AddSnapshotAsync("WVWZZZ1KZAW000001", "AACC", 2, "Before apply", new List<CodingPendingChange>());
        var list = await h.LoadSnapshotsAsync("WVWZZZ1KZAW000001");
        Check("two snapshots stored under override root", list.Count == 2 && Directory.Exists(tmp));
        Check("other VIN has its own history", (await h.LoadSnapshotsAsync("OTHER")).Count == 0);
        await h.DeleteSnapshotAsync("WVWZZZ1KZAW000001", s1);
        list = await h.LoadSnapshotsAsync("WVWZZZ1KZAW000001");
        Check("delete removes one", list.Count == 1 && list[0].CodingHex == "AACC");
        Directory.Delete(tmp, true);
        Console.WriteLine($"coding: {pass} pass, {fail} fail");
        return fail == 0 ? 0 : 1;
    }
    static bool Throws(Action a) { try { a(); return false; } catch { return true; } }
}
