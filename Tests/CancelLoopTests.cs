using MibExplorer.Models; using MibExplorer.Models.Scripting; using MibExplorer.Services; using MibExplorer.Services.Scripting; using MibExplorer.Settings;
public static class CancelLoopTests
{
    public static async Task<int> RunAsync(string[] a)
    {
        int port = int.Parse(a[1]); string dir = a[2]; int bad = 0;
        var svc = new SshMibConnectionService();
        await svc.ConnectAsync(new ConnectionSettings { UsePrivateKey = false, Host = "127.0.0.1", Port = port, Username = "root", Password = "secret" });
        var cat = new ScriptCatalogService(); // uses settings folder override set by caller dir
        for (int i = 0; i < 12; i++)
        {
            int delay = new[] { 0, 50, 150, 400, 800, 1500 }[i % 6];
            string name = "race" + i + ".sh"; string path = Path.Combine(dir, name);
            Directory.CreateDirectory(dir); File.WriteAllText(path, "#!/bin/sh\necho start\nsleep 20\n");
            var sc = new ScriptItem { Name = "race" + i, FileName = name, LocalPath = path };
            var exec = new ScriptExecutionService(svc); var cts = new CancellationTokenSource(); string remote = "";
            var t = exec.ExecuteAsync(sc, l => { if (l.Contains("Remote workspace:")) remote = l; if (l.Contains("Running script")) cts.CancelAfter(delay); }, cts.Token);
            string res; try { var r = await t; res = "finished " + r.ExitCode; } catch (ScriptCancelledException e) { res = "cancel term=" + e.Termination.Substring(0, 8) + " clean=" + e.WorkspaceCleanup; } catch (OperationCanceledException) { res = "plain-cancel"; }
            string now = (await svc.ExecuteCommandAsync("ls /tmp | grep race" + i + " | wc -l; true")).Trim();
            await Task.Delay(1500);
            string later = (await svc.ExecuteCommandAsync("ls /tmp | grep race" + i + " | wc -l; true")).Trim();
            Console.WriteLine($"iter {i} delay={delay}ms {res} leftoverNow={now} leftover+1.5s={later}");
            if (res.Contains("clean=removed") && (now != "0" || later != "0")) { bad++; Console.WriteLine("  ^ LEFTOVER DESPITE 'removed'"); }
            await svc.ExecuteCommandAsync("rm -f /tmp/*race" + i + ".sh; true");
        }
        try { await svc.ExecuteCommandAsync("pkill -f 'sleep [2]0'; true"); } catch { }
        Console.WriteLine("leftover-despite-removed count: " + bad); return bad == 0 ? 0 : 1;
    }
}
