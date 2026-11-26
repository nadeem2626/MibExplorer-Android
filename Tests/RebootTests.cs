using MibExplorer.Services;

public static class RebootTests
{
    static int pass, fail;
    static void Check(string n, bool ok) { if (ok) { pass++; Console.WriteLine("PASS " + n); } else { fail++; Console.WriteLine("FAIL " + n); } }
    static RebootWatcher W(Func<int, bool> up, out int[] calls) { var c = new int[1]; calls = c; return new RebootWatcher { Interval = TimeSpan.FromMilliseconds(5), DownTimeout = TimeSpan.FromMilliseconds(300), UpTimeout = TimeSpan.FromMilliseconds(300), Probe = (h, p, t) => Task.FromResult(up(c[0]++)) }; }
    public static async Task<int> RunAsync()
    {
        var w = W(n => n < 5 || n > 12, out _);
        Check("up, down, up = DownThenUp", await w.WatchAsync("x", 22) == RebootEvidence.DownThenUp);
        w = W(n => true, out _);
        Check("never goes down = None (a closed session is not evidence)", await w.WatchAsync("x", 22) == RebootEvidence.None);
        w = W(n => n < 3, out _);
        Check("goes down and stays down = WentDownOnly", await w.WatchAsync("x", 22) == RebootEvidence.WentDownOnly);
        w = W(n => false, out _);
        Check("already down at first probe still needs coming back", await w.WatchAsync("x", 22) == RebootEvidence.WentDownOnly);
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); l.Start(); int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        Check("default probe sees a listening port", await RebootWatcher.DefaultProbe("127.0.0.1", port, default));
        l.Stop();
        Check("default probe sees a closed port", !await RebootWatcher.DefaultProbe("127.0.0.1", port, default));
        Console.WriteLine($"reboot: {pass} pass, {fail} fail"); return fail == 0 ? 0 : 1;
    }
}
