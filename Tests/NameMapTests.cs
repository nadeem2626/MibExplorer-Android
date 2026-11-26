using MibExplorer.Services;

public static class NameMapTests
{
    static int pass, fail;
    static void Check(string n, bool ok) { if (ok) { pass++; Console.WriteLine("PASS " + n); } else { fail++; Console.WriteLine("FAIL " + n); } }
    public static Task<int> RunAsync()
    {
        var m = UploadNameMap.Build(new[] { "a.txt", "a.txt", "b;rm -rf.sh", "..", "x/y", "ctl\u0001.bin", "" });
        Check("plain name unchanged", m[0].Safe == "a.txt" && !m[0].Changed);
        Check("duplicate gets a suffix", m[1].Safe == "a_2.txt");
        Check("shell characters replaced", m[2].Safe == "b_rm -rf.sh" && m[2].Changed);
        Check("dot-dot becomes file", m[3].Safe == "file");
        Check("slash replaced", m[4].Safe == "x_y");
        Check("control char replaced", m[5].Safe == "ctl_.bin");
        Check("empty becomes file (second one suffixed)", m[6].Safe == "file_2");
        Check("all results are valid remote names", m.All(e => { try { WriteGuard.RequireName(e.Safe); return true; } catch { return false; } }));
        Check("long name is cut and keeps its extension", UploadNameMap.Build(new[] { new string('a', 300) + ".txt" })[0].Safe.EndsWith(".txt") && UploadNameMap.Build(new[] { new string('a', 300) + ".txt" })[0].Safe.Length <= 200);
        Console.WriteLine($"namemap: {pass} pass, {fail} fail"); return Task.FromResult(fail == 0 ? 0 : 1);
    }
}
