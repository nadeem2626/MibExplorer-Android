using MibExplorer.Models; using MibExplorer.Models.Scripting; using MibExplorer.Services; using MibExplorer.Services.Scripting; using MibExplorer.Settings;
public static class OfficialScriptsTests
{
    public static async Task<int> RunAsync(string[] a)
    {
        int fail = 0; void Check(string n, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + n); if (!ok) fail++; }
        string root = a.Length > 2 ? a[2] : Path.Combine(Path.GetTempPath(), "offscripts_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(root);
        AppSettingsStore.FolderOverride = Path.Combine(root, "settings");
        AppSettingsStore.Current.ScriptsFolderPath = Path.Combine(root, "scripts");
        var cat = new ScriptCatalogService(); var upd = new OfficialScriptUpdateService();
        Console.WriteLine("root " + root);
        Check("fresh catalog is empty", cat.GetScripts().Count == 0);
        string msg = ""; try { msg = await upd.UpdateOfficialScriptsAsync(cat.OfficialScriptsFolderPath); Console.WriteLine("UPDATE1: " + msg.Replace("\n", " | ")); } catch (Exception e) { Console.WriteLine("UPDATE1 EXC " + e.GetType().Name + " " + e.Message); fail++; }
        var items = cat.GetScripts();
        Console.WriteLine("scripts after update: " + items.Count);
        foreach (var s in items) Console.WriteLine($"  {s.Name} official={s.IsOfficial} pkg={s.IsPackage} type={s.ScriptType} ro={s.IsReadOnlyType} danger={s.IsDangerousType} ver={s.VersionDisplay} author={s.AuthorDisplay} modified={s.IsModified} desc=\"{(s.Description ?? "").Replace("\n"," ")}\"");
        Check("update downloaded at least one script", items.Count > 0);
        foreach (var s in items.Where(x => x.IsOfficial))
        {
            string? exp = upd.GetExpectedSha256(cat.OfficialScriptsFolderPath, s);
            string act = s.IsPackage ? ScriptIntegrityService.ComputePackageSha256(s.PackageRootPath) : ScriptIntegrityService.ComputeSingleScriptSha256(s.LocalPath);
            Console.WriteLine($"  integrity {s.Name}: expected={exp} actual={act}");
            Check("integrity matches manifest: " + s.Name, string.Equals(exp, act, StringComparison.OrdinalIgnoreCase));
        }
        try { string m2 = await upd.UpdateOfficialScriptsAsync(cat.OfficialScriptsFolderPath); Console.WriteLine("UPDATE2: " + m2.Replace("\n", " | ")); } catch (Exception e) { Console.WriteLine("UPDATE2 EXC " + e.Message); fail++; }
        var single = items.FirstOrDefault(x => x.IsOfficial && !x.IsPackage);
        if (single != null)
        {
            File.AppendAllText(single.LocalPath, "\necho tampered\n");
            string exp = upd.GetExpectedSha256(cat.OfficialScriptsFolderPath, single)!;
            Check("tampered official script detected", !string.Equals(ScriptIntegrityService.ComputeSingleScriptSha256(single.LocalPath), exp, StringComparison.OrdinalIgnoreCase));
            string r = await upd.RestoreOfficialScriptAsync(cat.OfficialScriptsFolderPath, single); Console.WriteLine("RESTORE: " + r);
            Check("restore returns matching hash", string.Equals(ScriptIntegrityService.ComputeSingleScriptSha256(single.LocalPath), exp, StringComparison.OrdinalIgnoreCase));
        }
        if (int.TryParse(a[1], out int port))
        {
            var svc = new SshMibConnectionService();
            await svc.ConnectAsync(new ConnectionSettings { UsePrivateKey = false, Host = "127.0.0.1", Port = port, Username = "root", Password = "secret" });
            await svc.ExecuteCommandAsync("rm -f /tmp/*_slow.sh /tmp/mibx_slow_done; true"); // stale files from earlier runs on the shared disposable server
            var exec = new ScriptExecutionService(svc);
            foreach (var sc in cat.GetScripts().Where(x => x.IsOfficial && !x.IsPackage))
            {
                var log = new List<string>(); var r = await exec.ExecuteAsync(sc, l => log.Add(l), CancellationToken.None);
                Console.WriteLine($"RUN official {sc.Name}: success={r.Success} exit={r.ExitCode} err='{r.ErrorMessage}' remote={r.RemoteScriptPath}");
                foreach (var l in log) Console.WriteLine("    | " + l.TrimEnd());
                Check("official read-only script ran through upload+exec, failure (no SD on the test host) reported as exit 1", !r.Success && r.ExitCode == 1 && log.Any(l => l.Contains("No SD card found")));
                string left = await svc.ExecuteCommandAsync("ls /tmp | grep -c " + System.IO.Path.GetFileNameWithoutExtension(sc.FileName) + " || true"); Console.WriteLine("    remote leftovers matching name: " + left.Trim());
            }
            // custom script: success path, output streaming, exit code
            string cdir = cat.CustomScriptsFolderPath; Directory.CreateDirectory(cdir);
            File.WriteAllText(Path.Combine(cdir, "ok.sh"), "#!/bin/sh\n# Type: ReadOnly\necho hello-from-script\nexit 0\n");
            File.WriteAllText(Path.Combine(cdir, "bad.sh"), "#!/bin/sh\necho failing\nexit 7\n");
            File.WriteAllText(Path.Combine(cdir, "slow.sh"), "#!/bin/sh\necho start\nsleep 25\necho finished-slow > /tmp/mibx_slow_done\n");
            foreach (var n in new[] { "ok", "bad" })
            {
                var sc = cat.GetScripts().First(x => x.Name == n); var log = new List<string>(); var r = await exec.ExecuteAsync(sc, l => log.Add(l), CancellationToken.None);
                Console.WriteLine($"RUN custom {n}: success={r.Success} exit={r.ExitCode} err='{r.ErrorMessage}'"); foreach (var l in log) Console.WriteLine("    | " + l.TrimEnd());
                Check("custom " + n + " exit code reported", n == "ok" ? r.Success && log.Any(l => l.Contains("hello-from-script")) : !r.Success && r.ExitCode == 7);
            }
            // cancel mid-run
            await svc.ExecuteCommandAsync("rm -f /tmp/mibx_slow_done");
            var slow = cat.GetScripts().First(x => x.Name == "slow"); var cts = new CancellationTokenSource(); var slog = new List<string>();
            var t = exec.ExecuteAsync(slow, l => { slog.Add(l); if (l.Contains("start")) cts.CancelAfter(1500); }, cts.Token);
            string outcome; try { var r = await t; outcome = "returned success=" + r.Success + " exit=" + r.ExitCode; } catch (OperationCanceledException ex2) { outcome = "OperationCanceledException:" + ex2.Message; Check("cancel reports termination+cleanup state", ex2 is ScriptCancelledException sce && sce.Termination.StartsWith("no process with the workspace path remains") && sce.WorkspaceCleanup == "removed"); }
            Console.WriteLine("CANCEL slow.sh: " + outcome + "; connected=" + svc.IsConnected);
            Console.WriteLine("IMMEDIATE after cancel: " + (await svc.ExecuteCommandAsync("ls -l /tmp | grep -i slow; echo; ps -ef | grep '[s]low' | head -5; true")).Replace("\n"," | "));
            await Task.Delay(27000);
            string done = await svc.ExecuteCommandAsync("ls /tmp/mibx_slow_done 2>&1 | head -1; ls /tmp | grep -i slow || true");
            Check("cancel stops script and removes workspace", done.Contains("No such file") && !done.Contains("_slow.sh")); Console.WriteLine("CANCEL aftermath on the unit (27s later): " + done.Replace("\n", " | "));
            try { await svc.ExecuteCommandAsync("rm -f /tmp/mibx_slow_done /tmp/*_slow.sh; true"); } catch (Exception cex) { Check("test cleanup " + cex.Message, false); }
            // failure paths: termination command and cleanup both fail -> exception must say so, not claim success
            {
                var px = System.Reflection.DispatchProxy.Create<IMibConnectionService, FailingProxy>(); var fp = (FailingProxy)(object)px; fp.Inner = svc;
                var ex2 = new ScriptExecutionService(px); var cts2 = new CancellationTokenSource();
                try { await ex2.ExecuteAsync(slow, l => { if (l.Contains("start")) cts2.CancelAfter(1000); }, cts2.Token); Check("cancel w/ failing kill+rm throws", false); }
                catch (ScriptCancelledException sce) { Console.WriteLine("CANCEL(fail kill+rm): " + sce.Message); Check("kill failure reported as unknown, cleanup failure as failed", sce.Termination == "unknown" && sce.WorkspaceCleanup == "failed"); }
                try { await svc.ExecuteCommandAsync("pkill -f '[_]slow.sh'; true"); await svc.ExecuteCommandAsync("rm -f /tmp/*_slow.sh /tmp/mibx_slow_done; true"); } catch (Exception cex) { Check("test cleanup " + cex.Message, false); }
            }

        }
        Console.WriteLine("FILES:"); foreach (var f in Directory.EnumerateFiles(cat.ScriptsFolderPath, "*", SearchOption.AllDirectories)) Console.WriteLine("  " + Path.GetRelativePath(cat.ScriptsFolderPath, f) + " " + new FileInfo(f).Length);
        return fail == 0 ? 0 : 1;
    }
}

public class FailingProxy : System.Reflection.DispatchProxy
{
    public IMibConnectionService Inner = null!;
    protected override object? Invoke(System.Reflection.MethodInfo? m, object?[]? a)
    {
        if (m!.Name == "ExecuteCommandAsync" && a![0] is string c && (c.Contains("pkill") || c.StartsWith("rm -rf")))
            return Task.FromException<string>(new InvalidOperationException("simulated failure"));
        try { return m.Invoke(Inner, a); } catch (System.Reflection.TargetInvocationException t) { throw t.InnerException!; }
    }
}
