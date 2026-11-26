using MibExplorer.Models; using MibExplorer.Services;
public static class SshOne
{
    public static async Task<int> RunAsync(string[] a)
    {
        var s = new SshMibConnectionService();
        await s.ConnectAsync(new ConnectionSettings { UsePrivateKey = false, Host = "127.0.0.1", Port = int.Parse(a[1]), Username = "root", Password = "secret" });
        Console.WriteLine("connected " + s.IsConnected);
        async Task Try(string n, Func<Task> f) { try { await f(); Console.WriteLine(n + ": ok, connected=" + s.IsConnected); } catch (Exception e) { Console.WriteLine(n + ": " + e.GetType().Name + " " + e.Message.Replace("\n", "|") + " connected=" + s.IsConnected); } }
        await Try("chmod missing", () => ((IRemotePermissionService)s).SetPermissionsAsync("/tmp/nope/zz", new PermissionBits(493)));
        await Try("exec", () => s.ExecuteCommandAsync("echo hi"));
        await Try("guard root", () => s.CreateDirectoryWithoutMountAsync("/topdir"));
        await Try("exec2", () => s.ExecuteCommandAsync("echo hi"));
        await Try("wrong pw", async () => { var b = new SshMibConnectionService(); await b.ConnectAsync(new ConnectionSettings { UsePrivateKey = false, Host = "127.0.0.1", Port = int.Parse(a[1]), Username = "root", Password = "bad" }); });
        return 0;
    }
}
