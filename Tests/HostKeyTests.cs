using MibExplorer.Services;

public static class HostKeyTests
{
    public static int Run()
    {
        int fail = 0; void Check(string n, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + n); if (!ok) fail++; }
        string f = Path.Combine(Path.GetTempPath(), "mibx-hk-" + Guid.NewGuid().ToString("N")[..6] + ".json");
        try
        {
            var v = new HostKeyVerifier(f);
            Check("empty store lists nothing", v.List().Count == 0);
            Check("new key without confirmation is rejected and not stored", !v.Verify("10.0.0.1:22", "ssh-rsa", "SHA256:aaa") && v.List().Count == 0);
            v.ConfirmNewKey = (_, _, _) => true;
            Check("confirmed new key accepted", v.Verify("10.0.0.2:22", "ssh-rsa", "SHA256:bbb") && v.Verify("10.0.0.1:22", "ssh-rsa", "SHA256:aaa"));
            Check("list is sorted and complete", v.List().Select(k => k.Key).SequenceEqual(new[] { "10.0.0.1:22", "10.0.0.2:22" }));
            Check("changed key is rejected even with confirmation", !v.Verify("10.0.0.1:22", "ssh-rsa", "SHA256:zzz"));
            Check("pins persist across instances", new HostKeyVerifier(f).List().Count == 2);
            v.Forget("10.0.0.1:22");
            Check("forget removes one host and persists", v.List().Count == 1 && new HostKeyVerifier(f).List().Single().Key == "10.0.0.2:22");
            Check("after forget the host is new again", v.Evaluate("10.0.0.1:22", "SHA256:zzz") == HostKeyDecision.NewKey);
        }
        finally { try { File.Delete(f); } catch { } }
        return fail == 0 ? 0 : 1;
    }
}
