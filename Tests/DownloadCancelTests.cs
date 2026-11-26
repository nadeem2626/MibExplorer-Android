using MibExplorer.Models; using MibExplorer.Services;
public static class DownloadCancelTests
{
    static int fail; static void Check(string n, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + n); if (!ok) fail++; }
    public static async Task<int> RunAsync(string[] a)
    {
        int port = int.Parse(a[1]); string root = a[2]; Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "big.bin"), new byte[150_000_000]);
        var svc = new SshMibConnectionService();
        await svc.ConnectAsync(new ConnectionSettings { UsePrivateKey = false, Host = "127.0.0.1", Port = port, Username = "root", Password = "secret" });
        string dir = Path.Combine(Path.GetTempPath(), "dlc_" + Guid.NewGuid().ToString("N")[..6]); Directory.CreateDirectory(dir);
        string local = Path.Combine(dir, "big.bin"); File.WriteAllText(local, "previous good copy");
        var cts = new CancellationTokenSource(); bool started = false;
        var prog = new Progress<FileTransferProgressInfo>(p => { if (!started && p.BytesTransferred > 1_000_000) { started = true; cts.Cancel(); } });
        try { await svc.DownloadFileAsync(Path.Combine(root, "big.bin"), local, prog, cts.Token); Check("cancelled download throws", false); } catch (OperationCanceledException) { Check("cancelled download throws OperationCanceled", true); } catch (Exception e) { Check("cancel surfaced as " + e.GetType().Name, started); }
        Check("progress reached cancel point", started);
        Check("previous local file is untouched after cancel", File.ReadAllText(local) == "previous good copy");
        Check("no .part leftover after cancel", !Directory.GetFiles(dir).Any(f => f.Contains(".part-")));
        await svc.DownloadFileAsync(Path.Combine(root, "big.bin"), local);
        Check("successful download replaces file with full content", new FileInfo(local).Length == 150_000_000 && !Directory.GetFiles(dir).Any(f => f.Contains(".part-")));
        Directory.Delete(dir, true); File.Delete(Path.Combine(root, "big.bin"));
        return fail;
    }
}
