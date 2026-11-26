using System.Net.Sockets;
using System.Text;
using MibExplorer.Models;

namespace MibExplorer.Services;

/// <summary>
/// Minimal FTP client (RFC 959, passive mode, plain text: FTP has no encryption). No shell: command execution,
/// terminal and mount handling are not available. Written for this project; tested only against a local fixture
/// server until it is run against a real unit.
/// </summary>
public sealed class FtpMibConnectionService : IMibConnectionService, IRemotePermissionService
{
    private TcpClient? _ctl;
    private StreamReader? _rd;
    private StreamWriter? _wr;
    private string _host = "";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsConnected => _ctl?.Connected == true;
    public event EventHandler<bool>? ConnectionStateChanged;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan ReplyTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public PermissionCapabilities PermissionCapabilities { get; } =
        new(CanChmod: true, CanChmodRecursive: false, CanChangeOwner: false,
            Note: "FTP: uses SITE CHMOD on one path. Many servers do not support it; the result is read back and checked. Recursive and owner changes unavailable.");

    public async Task ConnectAsync(ConnectionSettings s, CancellationToken ct = default)
    {
        Close();
        _host = s.Host;
        _ctl = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(Timeout);
        await _ctl.ConnectAsync(s.Host, s.Port, cts.Token);
        var ns = _ctl.GetStream();
        _rd = new StreamReader(ns, Encoding.UTF8); _wr = new StreamWriter(ns, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
        var hello = await ReadReplyAsync(cts.Token);
        if (hello.Code != 220) throw new IOException("FTP server refused: " + hello.Text);
        var user = string.IsNullOrEmpty(s.Username) ? "anonymous" : s.Username;
        var r = await CmdAsync("USER " + Clean(user), ct);
        if (r.Code == 331) r = await CmdAsync("PASS " + Clean(s.Password ?? ""), ct);
        if (r.Code is not (230 or 202)) throw new IOException("FTP login failed: " + r.Text);
        await CmdAsync("TYPE I", ct);
        ConnectionStateChanged?.Invoke(this, true);
    }

    private static string Clean(string v) => v.Any(char.IsControl) ? throw new ArgumentException("Invalid characters (control characters are never sent to the server)") : v;

    private record Reply(int Code, string Text);

    private async Task<Reply> ReadReplyAsync(CancellationToken ct)
    {
        using var tcts = CancellationTokenSource.CreateLinkedTokenSource(ct); tcts.CancelAfter(ReplyTimeout); ct = tcts.Token;
        string? line = await _rd!.ReadLineAsync(ct) ?? throw new IOException("FTP connection closed");
        if (line.Length < 3 || !int.TryParse(line.AsSpan(0, 3), out int code)) throw new IOException("Bad FTP reply: " + line);
        var sb = new StringBuilder(line);
        if (line.Length > 3 && line[3] == '-')
        {
            string prefix = line[..3] + " ";
            while (true)
            {
                string l2 = await _rd.ReadLineAsync(ct) ?? throw new IOException("FTP connection closed");
                sb.Append('\n').Append(l2);
                if (l2.StartsWith(prefix)) break;
            }
        }
        return new Reply(code, sb.ToString());
    }

    private async Task<Reply> CmdAsync(string cmd, CancellationToken ct)
    {
        if (_wr is null) throw new InvalidOperationException("Not connected");
        await _wr.WriteLineAsync(cmd.AsMemory(), ct);
        return await ReadReplyAsync(ct);
    }

    private async Task<TcpClient> OpenDataAsync(CancellationToken ct)
    {
        var r = await CmdAsync("PASV", ct);
        if (r.Code != 227) throw new IOException("PASV failed: " + r.Text);
        int a = r.Text.IndexOf('('), b = r.Text.IndexOf(')');
        var p = r.Text[(a + 1)..b].Split(',').Select(int.Parse).ToArray();
        if (p.Length != 6) throw new IOException("Bad PASV reply");
        var dc = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(Timeout);
        // Connect to the control host, not the address in the reply (NAT and wrong-IP servers).
        await dc.ConnectAsync(_host, p[4] * 256 + p[5], cts.Token);
        return dc;
    }

    private async Task<T> LockedAsync<T>(Func<Task<T>> f)
    {
        await _gate.WaitAsync();
        try { return await f(); }
        // A timeout or cancel leaves the control channel out of step with the server: drop the connection rather than guess.
        catch (OperationCanceledException) { Close(); throw; }
        finally { _gate.Release(); }
    }

    /// <summary>Applies the shared guard and refuses to write through a symlinked ancestor folder (FTP cannot resolve links).</summary>
    private async Task<string> GuardAsync(string path, bool create, CancellationToken ct)
    {
        string p = create ? WriteGuard.RequireCreate(path) : WriteGuard.RequireModify(path);
        string cur = "/";
        foreach (var seg in WriteGuard.Parent(p).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var kids = await GetChildrenAsync(cur, ct);
            var hit = kids.FirstOrDefault(k => k.Name == seg);
            if (hit is null) break;
            if (hit.EntryType == RemoteEntryType.Symlink) throw new InvalidOperationException("Refusing to write through the symlinked folder " + (cur == "/" ? "" : cur) + "/" + seg + " over FTP.");
            cur = (cur == "/" ? "" : cur) + "/" + seg;
        }
        return p;
    }

    private static string Path(string p) => string.IsNullOrEmpty(p) ? "/" : Clean(p);

    public Task<IReadOnlyList<RemoteExplorerItem>> GetRootEntriesAsync(CancellationToken ct = default) => GetChildrenAsync("/", ct);

    public Task<IReadOnlyList<RemoteExplorerItem>> GetChildrenAsync(string remotePath, CancellationToken ct = default) =>
        LockedAsync<IReadOnlyList<RemoteExplorerItem>>(async () =>
        {
            string path = Path(remotePath);
            using var dc = await OpenDataAsync(ct);
            var r = await CmdAsync("LIST " + path, ct);
            if (r.Code is not (125 or 150)) throw new IOException("LIST failed: " + r.Text);
            string raw;
            using (var rd = new StreamReader(dc.GetStream(), Encoding.UTF8)) raw = await rd.ReadToEndAsync(ct);
            var end = await ReadReplyAsync(ct);
            if (end.Code is not (226 or 250)) throw new IOException("LIST incomplete: " + end.Text);
            return SshMibConnectionService.ParseLsLa(path, raw);
        });

    public async Task<string> ReadRemoteTextFileAsync(string remotePath, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await RetrAsync(remotePath, ms, null, ct);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private Task<bool> RetrAsync(string remote, Stream dest, IProgress<FileTransferProgressInfo>? progress, CancellationToken ct) =>
        LockedAsync(async () =>
        {
            using var dc = await OpenDataAsync(ct);
            var r = await CmdAsync("RETR " + Path(remote), ct);
            if (r.Code is not (125 or 150)) throw new IOException("RETR failed: " + r.Text);
            var buf = new byte[32768]; ulong total = 0; int n;
            var ns = dc.GetStream();
            while ((n = await ns.ReadAsync(buf, ct)) > 0)
            {
                await dest.WriteAsync(buf.AsMemory(0, n), ct); total += (ulong)n;
                progress?.Report(new FileTransferProgressInfo { Operation = "Download", SourcePath = remote, BytesTransferred = total });
            }
            dc.Close();
            var end = await ReadReplyAsync(ct);
            if (end.Code is not (226 or 250)) throw new IOException("Download incomplete: " + end.Text);
            return true;
        });

    private Task<bool> StorAsync(Stream src, string remote, IProgress<FileTransferProgressInfo>? progress, CancellationToken ct) =>
        LockedAsync(async () =>
        {
            using var dc = await OpenDataAsync(ct);
            var r = await CmdAsync("STOR " + Path(remote), ct);
            if (r.Code is not (125 or 150)) throw new IOException("STOR failed: " + r.Text);
            var buf = new byte[32768]; ulong total = 0; int n;
            var ns = dc.GetStream();
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await ns.WriteAsync(buf.AsMemory(0, n), ct); total += (ulong)n;
                progress?.Report(new FileTransferProgressInfo { Operation = "Upload", DestinationPath = remote, BytesTransferred = total, TotalBytes = src.CanSeek ? (ulong)src.Length : null });
            }
            dc.Close();
            var end = await ReadReplyAsync(ct);
            if (end.Code is not (226 or 250)) throw new IOException("Upload incomplete: " + end.Text);
            return true;
        });

    public async Task DownloadFileAsync(string remotePath, string localPath, IProgress<FileTransferProgressInfo>? progress = null, CancellationToken ct = default)
    {
        string part = localPath + ".part";
        try { using (var fs = File.Create(part)) await RetrAsync(remotePath, fs, progress, ct); File.Move(part, localPath, true); }
        catch { try { File.Delete(part); } catch { } throw; }
    }

    public async Task UploadFileAsync(string localPath, string remotePath, IProgress<FileTransferProgressInfo>? progress = null, CancellationToken ct = default)
    {
        string p = await GuardAsync(remotePath, true, ct);
        if (await RemotePathExistsAsync(p, ct)) throw new IOException(p + " already exists. Overwriting is not offered over FTP.");
        string tmp = p + ".mibx-part"; long want = new FileInfo(localPath).Length;
        try
        {
            using (var fs = File.OpenRead(localPath)) await StorAsync(fs, tmp, progress, ct);
            string parent = WriteGuard.Parent(p);
            var item = (await GetChildrenAsync(parent, ct)).FirstOrDefault(x => x.Name == tmp[(tmp.LastIndexOf('/') + 1)..]);
            if (item is null || item.Size != want) throw new IOException($"Upload size check failed (server has {item?.Size.ToString() ?? "nothing"}, expected {want}).");
            await RenamePathFullAsync(tmp, p, ct);
        }
        catch { try { if (IsConnected) await DeleteRawAsync(tmp, ct); } catch { } throw; }
    }

    private Task DeleteRawAsync(string path, CancellationToken ct) => LockedAsync(async () => { await CmdAsync("DELE " + Path(path), ct); return true; });

    public Task UploadFileWithoutMountAsync(string localPath, string remotePath, IProgress<FileTransferProgressInfo>? progress = null, CancellationToken ct = default) =>
        UploadFileAsync(localPath, remotePath, progress, ct);

    public async Task<bool> RemotePathExistsAsync(string remotePath, CancellationToken ct = default)
    {
        string p = Path(remotePath).TrimEnd('/'); if (p == "") return true;
        int i = p.LastIndexOf('/'); string parent = i <= 0 ? "/" : p[..i], name = p[(i + 1)..];
        try { return (await GetChildrenAsync(parent, ct)).Any(x => x.Name == name); } catch (IOException) { return false; }
    }

    public Task ReplaceFileAsync(string localPath, string remotePath, IProgress<FileTransferProgressInfo>? progress = null, CancellationToken ct = default) =>
        throw new NotSupportedException("Replace is not offered over FTP (not atomic). Use SSH or Telnet.");

    private Task RenamePathFullAsync(string from, string to, CancellationToken ct) =>
        LockedAsync(async () =>
        {
            var r = await CmdAsync("RNFR " + Path(from), ct);
            if (r.Code != 350) throw new IOException("RNFR failed: " + r.Text);
            r = await CmdAsync("RNTO " + Path(to), ct);
            if (r.Code is not (250 or 200)) throw new IOException("RNTO failed: " + r.Text);
            return true;
        });

    public async Task RenamePathAsync(string remotePath, string newName, CancellationToken ct = default)
    {
        WriteGuard.RequireName(newName);
        string p = await GuardAsync(remotePath, false, ct);
        string target = WriteGuard.Parent(p) == "/" ? "/" + newName : WriteGuard.Parent(p) + "/" + newName;
        if (await RemotePathExistsAsync(target, ct)) throw new IOException("A file or folder named " + newName + " already exists.");
        await RenamePathFullAsync(p, target, ct);
    }

    public async Task DeleteFileAsync(string remotePath, CancellationToken ct = default)
    {
        string p = await GuardAsync(remotePath, false, ct);
        await LockedAsync(async () =>
        {
            var r = await CmdAsync("DELE " + p, ct);
            if (r.Code is not (250 or 200)) throw new IOException("DELE failed: " + r.Text);
            return true;
        });
    }

    public async Task DeletePathWithoutMountAsync(string remotePath, CancellationToken ct = default)
    {
        string p = await GuardAsync(remotePath, false, ct);
        await LockedAsync(async () =>
        {
            var r = await CmdAsync("RMD " + p, ct); // folders only, never recursive
            if (r.Code is 250 or 200) return true;
            r = await CmdAsync("DELE " + p, ct);
            if (r.Code is not (250 or 200)) throw new IOException("Delete failed (folders must be empty over FTP): " + r.Text);
            return true;
        });
    }

    public async Task CreateDirectoryWithoutMountAsync(string remotePath, CancellationToken ct = default)
    {
        string p = await GuardAsync(remotePath, true, ct);
        await LockedAsync(async () =>
        {
            var r = await CmdAsync("MKD " + p, ct);
            if (r.Code is not (257 or 250)) throw new IOException("MKD failed: " + r.Text);
            return true;
        });
    }

    public async Task MovePathWithoutMountAsync(string src, string dst, CancellationToken ct = default)
    {
        string a = await GuardAsync(src, false, ct), b = await GuardAsync(dst, true, ct);
        await RenamePathFullAsync(a, b, ct);
    }

    public async Task<bool> IsRemoteSymbolicLinkAsync(string remotePath, CancellationToken ct = default)
    {
        string p = Path(remotePath).TrimEnd('/'); int i = p.LastIndexOf('/');
        var items = await GetChildrenAsync(i <= 0 ? "/" : p[..i], ct);
        return items.FirstOrDefault(x => x.Name == p[(i + 1)..])?.EntryType == RemoteEntryType.Symlink;
    }

    public bool CanWriteToPath(string remotePath) => true; // the server enforces its own rules
    public Task<bool> CanWriteToPathAsync(string remotePath, CancellationToken ct = default) => Task.FromResult(true);
    public Task RunWritableOperationAsync(string remotePath, Func<CancellationToken, Task> op, CancellationToken ct = default) => op(ct);

    public async Task DownloadFilesBatchAsync(IReadOnlyList<(string RemotePath, string LocalPath, IProgress<FileTransferProgressInfo>? Progress)> files, CancellationToken ct = default)
    {
        foreach (var f in files) await DownloadFileAsync(f.RemotePath, f.LocalPath, f.Progress, ct);
    }

    public async Task UploadFilesBatchWithoutMountAsync(IReadOnlyList<(string LocalPath, string RemotePath, IProgress<FileTransferProgressInfo>? Progress)> files, CancellationToken ct = default)
    {
        foreach (var f in files) await UploadFileAsync(f.LocalPath, f.RemotePath, f.Progress, ct);
    }

    public async Task<PermissionBits> SetPermissionsAsync(string remotePath, PermissionBits mode, CancellationToken ct = default)
    {
        string p = await GuardAsync(remotePath, false, ct);
        var r = await LockedAsync(() => CmdAsync("SITE CHMOD " + mode.ToOctal() + " " + p, ct));
        if (r.Code is not (200 or 250)) throw new IOException("Server does not allow SITE CHMOD: " + r.Text);
        string t = p.TrimEnd('/'); int i = t.LastIndexOf('/');
        var item = (await GetChildrenAsync(i <= 0 ? "/" : t[..i], ct)).FirstOrDefault(x => x.Name == t[(i + 1)..]);
        var actual = PermissionBits.FromLsMode(item?.Mode) ?? throw new IOException("chmod sent but the new mode could not be read back.");
        if (actual.Mode != mode.Mode) throw new IOException($"chmod did not take effect: requested {mode.ToOctal()}, server lists {actual.ToOctal()}.");
        return actual;
    }

    public Task<bool> ProbeConnectionAsync(TimeSpan timeout, CancellationToken ct = default) =>
        LockedAsync(async () =>
        {
            try { using var c = CancellationTokenSource.CreateLinkedTokenSource(ct); c.CancelAfter(timeout); return (await CmdAsync("NOOP", c.Token)).Code == 200; }
            catch { return false; }
        });

    public Task<string> ExecuteCommandAsync(string command, CancellationToken ct = default) =>
        throw new NotSupportedException("FTP has no shell. Use a Telnet or SSH profile for commands.");

    public Task<IRemoteShellSession> CreateShellSessionAsync(CancellationToken ct = default) =>
        throw new NotSupportedException("FTP has no shell. Use a Telnet or SSH profile for the terminal.");

    public Task DisconnectAsync() { Close(); return Task.CompletedTask; }

    private void Close()
    {
        bool was = IsConnected;
        try { _wr?.WriteLine("QUIT"); } catch { }
        try { _ctl?.Close(); } catch { }
        _ctl = null; _rd = null; _wr = null;
        if (was) ConnectionStateChanged?.Invoke(this, false);
    }

    public void Dispose() => Close();
}
