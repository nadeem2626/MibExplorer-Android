using MibExplorer.Services;
public static class AtomicFileTests
{
    static int fail; static void Check(string n, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + n); if (!ok) fail++; }
    public static Task<int> RunAsync()
    {
        string dir = Path.Combine(Path.GetTempPath(), "atom_" + Guid.NewGuid().ToString("N")[..6]); Directory.CreateDirectory(dir); string f = Path.Combine(dir, "a.txt");
        File.WriteAllText(f, "old"); AtomicFile.WriteAllBytes(f, "new content"u8.ToArray());
        Check("replaces content", File.ReadAllText(f) == "new content"); Check("no staging files left", Directory.GetFiles(dir, ".*", SearchOption.TopDirectoryOnly).Length == 0 && Directory.GetFiles(dir).Length == 1);
        try { AtomicFile.WriteAllBytes(f, "BAD"u8.ToArray(), () => throw new IOException("injected failure before commit")); Check("injected failure propagates", false); } catch (IOException) { Check("injected failure propagates", true); }
        Check("old file intact after failure before commit", File.ReadAllText(f) == "new content"); Check("staging file removed after failure", Directory.GetFiles(dir).Length == 1 && !Directory.GetFiles(dir, ".*").Any());
        string other = Path.Combine(dir, ".a.txt.save-other"); File.WriteAllText(other, "someone else's staging");
        AtomicFile.WriteAllBytes(f, "third"u8.ToArray()); Check("another operation's staging file is not touched", File.ReadAllText(other) == "someone else's staging" && File.ReadAllText(f) == "third");
        try { AtomicFile.WriteAllBytes(Path.Combine(dir, "nodir", "x.txt"), new byte[1]); Check("missing folder fails", false); } catch (Exception) { Check("missing folder fails without touching anything", Directory.GetFiles(dir).Length == 2); }
        AtomicFile.WriteAllBytes(Path.Combine(dir, "fresh.txt"), "n"u8.ToArray()); Check("creates a new file when none exists", File.ReadAllText(Path.Combine(dir, "fresh.txt")) == "n");
        Directory.Delete(dir, true); return Task.FromResult(fail);
    }
}
