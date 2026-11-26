using System.Text.RegularExpressions;
using MibExplorer.Services;

public static class UninstallTests
{
    static int pass, fail;
    static void Check(string n, bool ok) { if (ok) { pass++; Console.WriteLine("PASS " + n); } else { fail++; Console.WriteLine("FAIL " + n); } }
    static string RepoRoot() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !File.Exists(Path.Combine(d.FullName, "MibExplorer.sln"))) d = d.Parent; return d?.FullName ?? Directory.GetCurrentDirectory(); }
    public static Task<int> RunAsync()
    {
        string srcPath = Path.Combine(RepoRoot(), "MibExplorer", "Views", "MainWindow", "MainWindow.xaml.cs");
        if (!File.Exists(srcPath)) { Console.WriteLine("SKIP uninstall command comparison: needs the original desktop source file " + srcPath + " (not part of this repository). Place it there to run this check."); return Task.FromResult(0); }
        string src = File.ReadAllText(srcPath);
        int a = src.IndexOf("string command =\n                \"export PATH=/proc/boot"); if (a < 0) a = src.IndexOf("string command =\r\n                \"export PATH=/proc/boot");
        int b = src.IndexOf("\"echo OK\";", a) + 10;
        var parts = Regex.Matches(src.Substring(a, b - a), "^\\s+\"(.*)\"\\s*\\+?;?\\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\"));
        Check("command equals the upstream desktop command", string.Concat(parts) == DirectSshUninstall.Command);
        // Result interpretation: the trailing "echo OK" alone must not read as verified cleanup.
        Check("interpret: clean OK is PrintedOk (still not proof)", DirectSshUninstall.Interpret("OK\n", false) == DirectSshUninstall.Outcome.PrintedOk);
        Check("interpret: OK after errors is flagged", DirectSshUninstall.Interpret("rm: /x: No such file\nmount: Permission denied\nOK\n", false) == DirectSshUninstall.Outcome.PrintedOkWithErrors);
        Check("interpret: no OK -> NoOk", DirectSshUninstall.Interpret("mount: failed\n", false) == DirectSshUninstall.Outcome.NoOk);
        Check("interpret: empty output -> NoOk", DirectSshUninstall.Interpret("", false) == DirectSshUninstall.Outcome.NoOk && DirectSshUninstall.Interpret(null, false) == DirectSshUninstall.Outcome.NoOk);
        Check("interpret: word containing OK is not OK", DirectSshUninstall.Interpret("BROKEN\n", false) == DirectSshUninstall.Outcome.NoOk && DirectSshUninstall.Interpret("OK but more\n", false) == DirectSshUninstall.Outcome.NoOk);
        Check("interpret: dropped connection overrides output", DirectSshUninstall.Interpret("OK\n", true) == DirectSshUninstall.Outcome.ConnectionDropped);
        foreach (var o in Enum.GetValues<DirectSshUninstall.Outcome>()) Check("describe " + o + " mentions the startup.sh hook that stays", DirectSshUninstall.Describe(o, "x").Contains("startup.sh"));
        Check("describe PrintedOk does not claim success of every step", !DirectSshUninstall.Describe(DirectSshUninstall.Outcome.PrintedOk, "OK").Contains("removed successfully") && DirectSshUninstall.Describe(DirectSshUninstall.Outcome.PrintedOk, "OK").Contains("does not check each step"));
        string c = DirectSshUninstall.Command;
        Check("starts with a fixed PATH and ends with echo OK", c.StartsWith("export PATH=/proc/boot:") && c.EndsWith("echo OK"));
        Check("no command substitution or user placeholders", !c.Contains("$(") && !c.Contains("`") && !c.Contains("{0}"));
        Check("remounts everything it opened back to read-only", c.Contains("mount -ur /mnt/system") && c.Contains("mount -ur /mnt/app") && c.Contains("mount -ur $EFS_PERSIST"));
        Check("restores inetd and pf from backup before deleting", c.IndexOf("INETD_CONF_BU") < c.IndexOf("rm -f \"$ROOT_AUTH_KEYS\""));
        Check("lists what it removes", DirectSshUninstall.Effects.Length == 8);
        try
        {
            string d = "/tmp/kitest"; if (Directory.Exists(d)) Directory.Delete(d, true); Directory.CreateDirectory(d);
            foreach (var pass in new[] { "", "s3cret pass" })
            {
                var psi = new System.Diagnostics.ProcessStartInfo("ssh-keygen", $"-q -t rsa -b 2048 -m PEM -N \"{pass}\" -f {d}/k{pass.Length}") { RedirectStandardError = true }; System.Diagnostics.Process.Start(psi)!.WaitForExit();
                string pem = File.ReadAllText($"{d}/k{pass.Length}"); string pubRef = File.ReadAllText($"{d}/k{pass.Length}.pub").Split(' ')[1];
                var r = SshKeyService.InspectImported(pem, pass);
                Check($"import key (passphrase {(pass == "" ? "none" : "set")}): accepted and public key equals ssh-keygen", r.Valid && r.PublicOpenSsh != null && r.PublicOpenSsh.Split(' ')[1] == pubRef);
                if (pass != "") Check("import key: wrong passphrase rejected", !SshKeyService.InspectImported(pem, "wrong").Valid);
            }
            Check("import garbage rejected", !SshKeyService.InspectImported("not a key", "").Valid);
        }
        catch (Exception ex) { Check("key import test setup: " + ex.GetType().Name, false); }
        string feed = "[{\"name\":\"v0.7.0\"},{\"name\":\"v0.6.1\"},{\"name\":\"v0.8.0-beta.1\"},{\"name\":\"junk\"},{\"name\":\"v0.10.0\"}]";
        Check("upstream feed: latest stable picked numerically (0.10.0 > 0.7.0)", UpstreamVersionCheck.Latest(feed) == "v0.10.0");
        Check("upstream feed: prerelease ignored by default, used when asked", UpstreamVersionCheck.Latest("[{\"name\":\"v0.7.0\"},{\"name\":\"v0.8.0-beta.1\"}]") == "v0.7.0" && UpstreamVersionCheck.Latest("[{\"name\":\"v0.7.0\"},{\"name\":\"v0.8.0-beta.1\"}]", true) == "v0.8.0-beta.1");
        Check("upstream feed: newer comparison", UpstreamVersionCheck.IsNewer("v0.7.1", "v0.7.0") && !UpstreamVersionCheck.IsNewer("v0.7.0", "v0.7.0"));
        Console.WriteLine($"uninstall: {pass} pass, {fail} fail"); return Task.FromResult(fail == 0 ? 0 : 1);
    }
}
