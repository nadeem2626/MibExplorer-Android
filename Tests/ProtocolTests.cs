using System.Diagnostics;
using MibExplorer.Models;
using MibExplorer.Services;

public static class ProtocolTests
{
    static int fails = 0;
    static void Check(string name, bool ok, string? detail = null) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok || detail == null ? "" : "  -> " + detail)); if (!ok) fails++; }

    static async Task<bool> Throws<T>(Func<Task> f) where T : Exception { try { await f(); return false; } catch (T) { return true; } catch (Renci.SshNet.Common.SshException) when (typeof(T) == typeof(IOException)) { return true; } catch { return false; } }

    public static async Task<int> RunAsync(string[] args)
    {
        string fx = args[1];
        // --- pure permission logic ---
        Check("octal 755 -> rwxr-xr-x", new PermissionBits(Convert.ToInt32("755", 8)).ToSymbolic() == "-rwxr-xr-x");
        Check("ls mode parse drwxr-xr-x = 755", PermissionBits.FromLsMode("drwxr-xr-x")?.ToOctal() == "755");
        Check("setuid rwsr-xr-x = 4755", PermissionBits.FromLsMode("-rwsr-xr-x")?.ToOctal() == "4755");
        Check("sticky rwxrwxrwt = 1777", PermissionBits.FromLsMode("drwxrwxrwt")?.ToOctal() == "1777");
        Check("S without x = 4644", PermissionBits.FromLsMode("-rwSr--r--")?.ToOctal() == "4644");
        Check("garbage mode rejected", PermissionBits.FromLsMode("hello world") is null && PermissionBits.FromLsMode("-rwx") is null);
        Check("octal parse rejects 758, 12, 99999, ' 755'", !PermissionBits.TryParseOctal("758", out _) && !PermissionBits.TryParseOctal("12", out _) && !PermissionBits.TryParseOctal("99999", out _) && !PermissionBits.TryParseOctal(" 755", out _));
        Check("octal parse accepts 0644 and 644", PermissionBits.TryParseOctal("0644", out var a) && PermissionBits.TryParseOctal("644", out var b) && a.Equals(b));
        var g = new PermissionBits(0).With(0, 0, true).With(0, 1, true).With(1, 0, true).With(2, 0, true);
        Check("grid toggles build 644", g.ToOctal() == "644", g.ToOctal());
        Check("symbolic round trip all 0..0xFFF", Enumerable.Range(0, 4096).All(m => PermissionBits.FromLsMode(new PermissionBits(m).ToSymbolic())?.Mode == m));
        Check("chmod command quotes path", ChmodCommand.Build(new PermissionBits(493), "/a b/it's") == "chmod 755 '/a b/it'\"'\"'s'", ChmodCommand.Build(new PermissionBits(493), "/a b/it's"));
        Check("chmod rejects control chars", await Throws<ArgumentException>(() => { ChmodCommand.Build(new PermissionBits(0), "/a\n;rm -rf /"); return Task.CompletedTask; }));
        Check("readback parse", ChmodCommand.ParseReadBack("-rw-r--r--    1 root root 5 Nov 25 09:00 x\n")?.ToOctal() == "644");

        string work = Path.Combine(Path.GetTempPath(), "mxproto" + Guid.NewGuid().ToString("N")[..6]);
        string root = Path.Combine(work, "root"); Directory.CreateDirectory(Path.Combine(root, "etc")); Directory.CreateDirectory(Path.Combine(root, "dir with space"));
        File.WriteAllText(Path.Combine(root, "etc", "hello.txt"), "hello unit\n");
        var bin = new byte[3000]; new Random(7).NextBytes(bin); File.WriteAllBytes(Path.Combine(root, "etc", "blob.bin"), bin);
        File.WriteAllBytes(Path.Combine(root, "etc", "ff.bin"), new byte[] { 255, 0, 255, 255, 10, 13, 39, 92 });
        Process.Start("chmod", "640 " + Path.Combine(root, "etc", "hello.txt"))!.WaitForExit();
        var procs = new List<Process>();
        Process Start(string script, params string[] a) { var p = Process.Start(new ProcessStartInfo("python3", script + " " + string.Join(" ", a)) { WorkingDirectory = fx })!; procs.Add(p); return p; }
        try
        {
            Start("fake_ftpd.py", "32121", root); Start("fake_ftpd.py", "32122", root, "nochmod"); Start("fake_telnetd.py", "32123", root);
            await Task.Delay(1000);
            await Suite("FTP", () => new FtpMibConnectionService(), 32121, "admin", "pw", root, work, true);
            using (var f = new FtpMibConnectionService())
            {
                await f.ConnectAsync(new ConnectionSettings { UsePrivateKey = false, Host = "127.0.0.1", Port = 32122, Username = "admin", Password = "pw" });
                Check("FTP chmod unsupported server -> clear error", await Throws<IOException>(() => f.SetPermissionsAsync("/etc/hello.txt", new PermissionBits(493))));
            }
            File.WriteAllText(Path.Combine(root, "etc", "hello.txt"), "hello unit\n"); Process.Start("chmod", "640 " + Path.Combine(root, "etc", "hello.txt"))!.WaitForExit();
            await Suite("Telnet", () => new TelnetMibConnectionService(), 32123, "root", "secret", root, work, false);
            if (args.Length > 2 && args[2] == "indep")
            {
                Start("indep_ftpd.py", "32131", root); Start("indep_telnetd.py", "32132", root); Start("indep_sshd.py", "32141", root);
                await Task.Delay(2500);
                File.WriteAllText(Path.Combine(root, "etc", "hello.txt"), "hello unit\n"); Process.Start("chmod", "640 " + Path.Combine(root, "etc", "hello.txt"))!.WaitForExit();
                await Suite("pyftpdlib FTP", () => new FtpMibConnectionService(), 32131, "admin", "pw", root, work, true);
                File.WriteAllText(Path.Combine(root, "etc", "hello.txt"), "hello unit\n"); Process.Start("chmod", "640 " + Path.Combine(root, "etc", "hello.txt"))!.WaitForExit();
                await Suite("telnetlib3 Telnet", () => new TelnetMibConnectionService(), 32132, "root", "secret", root, work, false);
                File.WriteAllText(Path.Combine(root, "etc", "hello.txt"), "hello unit\n"); Process.Start("chmod", "640 " + Path.Combine(root, "etc", "hello.txt"))!.WaitForExit();
                {
                    var (kpem, kpub) = SshKeyService.GenerateInMemory("t"); string akf = Path.Combine(fx, "indep_authorized.pub"); File.WriteAllText(akf, kpub);
                    Start("indep_sshd.py", "32143", root); await Task.Delay(1500);
                    using var ks = new SshMibConnectionService();
                    await ks.ConnectAsync(new ConnectionSettings { UsePrivateKey = true, PrivateKeyPem = kpem, Host = "127.0.0.1", Port = 32143, Username = "root" });
                    Check("asyncssh SSH: public-key login with the app's generated key", ks.IsConnected && (await ks.ExecuteCommandAsync("echo keyok")).Contains("keyok"));
                    using var ks2 = new SshMibConnectionService(); var (bpem, _) = SshKeyService.GenerateInMemory("other");
                    Check("asyncssh SSH: a different key is rejected", await Throws<IOException>(() => ks2.ConnectAsync(new ConnectionSettings { UsePrivateKey = true, PrivateKeyPem = bpem, Host = "127.0.0.1", Port = 32143, Username = "root" })));
                    File.Delete(akf);
                    var pin = new HostKeyVerifier(Path.Combine(work, "kh.json")) { ConfirmNewKey = (h, al, fp) => true };
                    using var k3 = new SshMibConnectionService { HostKeys = pin, RequireHostKeyPinning = true };
                    await k3.ConnectAsync(new ConnectionSettings { UsePrivateKey = false, Host = "127.0.0.1", Port = 32141, Username = "root", Password = "secret" });
                    Check("asyncssh SSH: first connect pins the host key", k3.IsConnected && File.Exists(Path.Combine(work, "kh.json"))); await k3.DisconnectAsync();
                    using var k4 = new SshMibConnectionService { HostKeys = pin, RequireHostKeyPinning = true };
                    await k4.ConnectAsync(new ConnectionSettings { UsePrivateKey = false, Host = "127.0.0.1", Port = 32141, Username = "root", Password = "secret" });
                    Check("asyncssh SSH: second connect with the pinned key works", k4.IsConnected); await k4.DisconnectAsync();
                }
                await Suite("asyncssh SSH", () => new SshMibConnectionService(), 32141, "root", "secret", root, work, false);
            }
        }
        finally { foreach (var p in procs) { try { p.Kill(); } catch { } } }
        Console.WriteLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }

    static async Task Suite(string tag, Func<IMibConnectionService> make, int port, string user, string pw, string root, string work, bool ftp)
    {
        string X = ftp ? "" : root; await Suite2(tag, make, port, user, pw, root, work, ftp, X);
    }
    static async Task Suite2(string tag, Func<IMibConnectionService> make, int port, string user, string pw, string root, string work, bool ftp, string X)
    {
        string T(string s) => tag + ": " + s;
        using (var bad = (IDisposable)make())
            Check(T("wrong password rejected"), await Throws<IOException>(() => ((IMibConnectionService)bad).ConnectAsync(new ConnectionSettings { UsePrivateKey = false, Host = "127.0.0.1", Port = port, Username = user, Password = "nope" })));
        var svc = make();
        await svc.ConnectAsync(new ConnectionSettings { UsePrivateKey = false, Host = "127.0.0.1", Port = port, Username = user, Password = pw });
        Check(T("connected"), svc.IsConnected);
        var root0 = await svc.GetChildrenAsync(X + "/");
        Check(T("list root shows etc + dir with space"), root0.Any(x => x.Name == "etc" && x.IsDirectory) && root0.Any(x => x.Name == "dir with space"), string.Join(",", root0.Select(x => x.Name)));
        var etc = await svc.GetChildrenAsync(X + "/etc");
        var h = etc.FirstOrDefault(x => x.Name == "hello.txt");
        Check(T("list /etc has hello.txt mode 640"), h != null && PermissionBits.FromLsMode(h.Mode)?.ToOctal() == "640", h?.Mode);
        Check(T("owner parsed"), !string.IsNullOrEmpty(h?.Owner));
        Check(T("read text file"), (await svc.ReadRemoteTextFileAsync(X + "/etc/hello.txt")).Trim() == "hello unit");
        string dl = Path.Combine(work, tag + "-blob"); await svc.DownloadFileAsync(X + "/etc/blob.bin", dl);
        Check(T("download binary 3000 bytes identical"), File.ReadAllBytes(dl).SequenceEqual(File.ReadAllBytes(Path.Combine(root, "etc", "blob.bin"))));
        await svc.DownloadFileAsync(X + "/etc/ff.bin", dl);
        Check(T("download special bytes (0xFF, CR, LF, quote, backslash)"), File.ReadAllBytes(dl).SequenceEqual(File.ReadAllBytes(Path.Combine(root, "etc", "ff.bin"))));
        string up = Path.Combine(work, tag + "-up"); var upData = new byte[2500]; new Random(3).NextBytes(upData); upData[0] = 255; upData[1] = 39; upData[2] = 92; upData[3] = 37; File.WriteAllBytes(up, upData);
        await svc.UploadFileAsync(up, X + "/dir with space/up.bin");
        Check(T("upload 2500 bytes incl. special bytes identical"), File.ReadAllBytes(Path.Combine(root, "dir with space", "up.bin")).SequenceEqual(upData));
        await svc.CreateDirectoryWithoutMountAsync(X + "/dir with space/newdir");
        Check(T("mkdir"), Directory.Exists(Path.Combine(root, "dir with space", "newdir")));
        await svc.RenamePathAsync(X + "/dir with space/newdir", "renamed");
        Check(T("rename"), Directory.Exists(Path.Combine(root, "dir with space", "renamed")) && !Directory.Exists(Path.Combine(root, "dir with space", "newdir")));
        Check(T("exists true/false"), await svc.RemotePathExistsAsync(X + "/dir with space/renamed") && !await svc.RemotePathExistsAsync(X + "/nonexistent"));
        File.WriteAllText(up, "new content");
        if (ftp) Check(T("replace refused (not atomic)"), await Throws<NotSupportedException>(() => svc.ReplaceFileAsync(up, X + "/etc/hello.txt")));
        else
        {
            await svc.ReplaceFileAsync(up, X + "/etc/hello.txt");
            Check(T("replace file"), File.ReadAllText(Path.Combine(root, "etc", "hello.txt")) == "new content" && !Directory.GetFiles(Path.Combine(root, "etc")).Any(f => f.Contains("mibx") || f.Contains("__mibexplorer")));
        }
        await svc.DeletePathWithoutMountAsync(X + "/dir with space/renamed");
        Check(T("delete empty dir"), !Directory.Exists(Path.Combine(root, "dir with space", "renamed")));
        await svc.DeleteFileAsync(X + "/dir with space/up.bin");
        Check(T("delete file"), !File.Exists(Path.Combine(root, "dir with space", "up.bin")));
        var perm = (IRemotePermissionService)svc;
        Check(T("recursive reported unavailable"), !perm.PermissionCapabilities.CanChmodRecursive && perm.PermissionCapabilities.CanChmod);
        var res = await perm.SetPermissionsAsync(X + "/etc/blob.bin", new PermissionBits(Convert.ToInt32("755", 8)));
        string real = Convert.ToString(File.GetUnixFileMode(Path.Combine(root, "etc", "blob.bin")) is var m ? (int)m : 0, 8);
        Check(T("chmod 755 applied on disk and read back"), res.ToOctal() == "755" && real == "755", real);
        await perm.SetPermissionsAsync(X + "/etc/blob.bin", new PermissionBits(Convert.ToInt32("600", 8)));
        Check(T("chmod 600 applied on disk"), Convert.ToString((int)File.GetUnixFileMode(Path.Combine(root, "etc", "blob.bin")), 8) == "600");
        Check(T("chmod on missing path errors"), await Throws<IOException>(() => perm.SetPermissionsAsync(X + "/etc/nope", new PermissionBits(493))));
        Check(T("guard: root and top-level mutations refused"), await Throws<InvalidOperationException>(() => svc.CreateDirectoryWithoutMountAsync("/topdir")) && await Throws<InvalidOperationException>(() => svc.DeleteFileAsync("/etc")) && await Throws<InvalidOperationException>(() => svc.RenamePathAsync("/etc", "etc2")) && await Throws<InvalidOperationException>(() => perm.SetPermissionsAsync("/etc", new PermissionBits(493))));
        Check(T("guard: protected /proc and /dev refused, .. and relative refused"), await Throws<InvalidOperationException>(() => svc.CreateDirectoryWithoutMountAsync("/dev/zz/x")) && await Throws<InvalidOperationException>(() => svc.DeleteFileAsync("/proc/1/x")) && await Throws<ArgumentException>(() => svc.DeleteFileAsync("/etc/../etc/x")) && await Throws<ArgumentException>(() => svc.DeleteFileAsync("etc/x")));
        Check(T("guard: CR/LF/NUL/Ctrl-C in path rejected"), await Throws<ArgumentException>(() => svc.DeleteFileAsync("/etc/a\r\nDELE /etc/hello.txt")) && await Throws<ArgumentException>(() => svc.CreateDirectoryWithoutMountAsync("/etc/a\0b")) && await Throws<ArgumentException>(() => svc.CreateDirectoryWithoutMountAsync("/etc/a\u0003b")) && File.Exists(Path.Combine(root, "etc", "blob.bin")));
        Directory.CreateDirectory(Path.Combine(root, "dir with space", "full")); File.WriteAllText(Path.Combine(root, "dir with space", "full", "x.txt"), "x");
        Check(T("delete non-empty folder refused, content kept"), await Throws<Exception>(() => SafeOps.DeleteAsync(svc, new RemoteExplorerItem { Name = "full", FullPath = X + "/dir with space/full", EntryType = RemoteEntryType.Directory })) && File.Exists(Path.Combine(root, "dir with space", "full", "x.txt")));
        Directory.CreateDirectory(Path.Combine(root, "dir with space", "emptyd")); await SafeOps.DeleteAsync(svc, new RemoteExplorerItem { Name = "emptyd", FullPath = X + "/dir with space/emptyd", EntryType = RemoteEntryType.Directory });
        Check(T("delete empty folder ok"), !Directory.Exists(Path.Combine(root, "dir with space", "emptyd")));
        {
            string D = Path.Combine(root, "dir with space"); string XD = X + "/dir with space";
            RemoteExplorerItem F(string n, string full, bool dir = false) => new RemoteExplorerItem { Name = n, FullPath = full, EntryType = dir ? RemoteEntryType.Directory : RemoteEntryType.File };
            File.WriteAllBytes(Path.Combine(D, "cp src.bin"), upData); Directory.CreateDirectory(Path.Combine(D, "cpdest")); Directory.CreateDirectory(Path.Combine(D, "mvdest"));
            await SafeOps.CopyAsync(svc, F("cp src.bin", XD + "/cp src.bin"), XD + "/cpdest");
            Check(T("copy file into folder: identical bytes, source kept"), File.ReadAllBytes(Path.Combine(D, "cpdest", "cp src.bin")).SequenceEqual(upData) && File.Exists(Path.Combine(D, "cp src.bin")));
            await SafeOps.CopyAsync(svc, F("cp src.bin", XD + "/cp src.bin"), XD, "cp copy.bin");
            Check(T("copy under a new name in the same folder"), File.ReadAllBytes(Path.Combine(D, "cp copy.bin")).SequenceEqual(upData));
            File.WriteAllText(Path.Combine(D, "cpdest", "keep.txt"), "keep");
            Check(T("copy onto existing refused, existing untouched"), await Throws<IOException>(() => SafeOps.CopyAsync(svc, F("cp src.bin", XD + "/cp src.bin"), XD + "/cpdest")) && File.ReadAllBytes(Path.Combine(D, "cpdest", "cp src.bin")).SequenceEqual(upData));
            Check(T("copy leaves no temp files behind"), !Directory.GetFiles(Path.Combine(D, "cpdest"), ".mibx-cp-*").Any() && !Directory.GetFiles(D, ".mibx-cp-*").Any());
            Check(T("copy folder refused"), await Throws<NotSupportedException>(() => SafeOps.CopyAsync(svc, F("cpdest", XD + "/cpdest", true), XD + "/mvdest")));
            Check(T("copy to protected /proc refused"), await Throws<InvalidOperationException>(() => SafeOps.CopyAsync(svc, F("cp src.bin", XD + "/cp src.bin"), "/proc")));
            await SafeOps.MoveAsync(svc, F("cp copy.bin", XD + "/cp copy.bin"), XD + "/mvdest");
            Check(T("move file: arrives, source gone"), File.ReadAllBytes(Path.Combine(D, "mvdest", "cp copy.bin")).SequenceEqual(upData) && !File.Exists(Path.Combine(D, "cp copy.bin")));
            await SafeOps.MoveAsync(svc, F("cpdest", XD + "/cpdest", true), XD + "/mvdest");
            Check(T("move folder with content"), File.Exists(Path.Combine(D, "mvdest", "cpdest", "keep.txt")) && !Directory.Exists(Path.Combine(D, "cpdest")));
            Check(T("move folder into itself refused"), await Throws<InvalidOperationException>(() => SafeOps.MoveAsync(svc, F("mvdest", XD + "/mvdest", true), XD + "/mvdest/cpdest")));
            File.WriteAllText(Path.Combine(D, "mvdest", "clash.txt"), "A"); File.WriteAllText(Path.Combine(D, "clash.txt"), "B");
            Check(T("move onto existing refused, both kept"), await Throws<IOException>(() => SafeOps.MoveAsync(svc, F("clash.txt", XD + "/clash.txt"), XD + "/mvdest")) && File.ReadAllText(Path.Combine(D, "mvdest", "clash.txt")) == "A" && File.ReadAllText(Path.Combine(D, "clash.txt")) == "B");
            Check(T("move to same folder refused"), await Throws<InvalidOperationException>(() => SafeOps.MoveAsync(svc, F("clash.txt", XD + "/clash.txt"), XD)));
            Check(T("move out of protected /proc refused"), await Throws<InvalidOperationException>(() => SafeOps.MoveAsync(svc, F("x", "/proc/x"), XD)));
            foreach (var n in new[] { "mvdest", "cpdest" }) if (Directory.Exists(Path.Combine(D, n))) Directory.Delete(Path.Combine(D, n), true);
            foreach (var n in new[] { "clash.txt", "cp src.bin", "cp copy.bin" }) File.Delete(Path.Combine(D, n));
        }
        File.Delete(Path.Combine(root, "etc", "lnk")); File.CreateSymbolicLink(Path.Combine(root, "etc", "lnk"), Path.Combine(root, "etc", "blob.bin"));
        Check(T("chmod on symlink refused"), await Throws<InvalidOperationException>(() => SafeOps.ChmodAsync(svc, new RemoteExplorerItem { Name = "lnk", FullPath = X + "/etc/lnk", EntryType = RemoteEntryType.Symlink }, new PermissionBits(493))));
        string lroot = Path.Combine(root, "linkdir"); File.Delete(lroot); File.CreateSymbolicLink(lroot, "/proc");
        Check(T("write through symlink into /proc refused"), await Throws<Exception>(() => svc.CreateDirectoryWithoutMountAsync(X + "/linkdir/zz")) && !Directory.Exists("/proc/zz"));
        File.Delete(lroot);
        foreach (var oct in new[] { "4755", "2755", "1777", "6755", "0600" })
        {
            string tgt = Path.Combine(root, "etc", "sp.bin"); File.WriteAllText(tgt, "x");
            PermissionBits.TryParseOctal(oct, out var want);
            var got = await perm.SetPermissionsAsync(X + "/etc/sp.bin", want);
            string disk = Convert.ToString(Convert.ToInt32(Process.Start(new ProcessStartInfo("stat", "-c %a " + tgt) { RedirectStandardOutput = true })!.StandardOutput.ReadToEnd().Trim(), 8), 8);
            Check(T("chmod " + oct + " special bits applied, read back = " + got.ToOctal()), got.Mode == want.Mode && PermissionBits.TryParseOctal(disk, out var dsk) && dsk.Mode == want.Mode, disk);
        }
        {
            string pd = Path.Combine(work, tag + "-folderdl"); if (Directory.Exists(pd)) Directory.Delete(pd, true);
            var fr = await SafeOps.DownloadFolderAsync(svc, X + "/etc", pd);
            Check(T("folder download: files identical, symlink skipped"), File.ReadAllBytes(Path.Combine(pd, "blob.bin")).SequenceEqual(File.ReadAllBytes(Path.Combine(root, "etc", "blob.bin"))) && fr.Files >= 3 && fr.Skipped.Any(x => x.Contains("symlink")) && !File.Exists(Path.Combine(pd, "lnk")), string.Join("|", fr.Skipped));
            string src = Path.Combine(work, tag + "-folderup"); if (Directory.Exists(src)) Directory.Delete(src, true);
            Directory.CreateDirectory(Path.Combine(src, "sub dir")); File.WriteAllBytes(Path.Combine(src, "a.bin"), new byte[] { 0, 255, 10, 13, 39 }); File.WriteAllText(Path.Combine(src, "sub dir", "b it.txt"), "bee");
            var ur = await SafeOps.UploadFolderAsync(svc, src, X + "/dir with space/upfolder");
            Check(T("folder upload: tree and bytes identical"), ur.Files == 2 && ur.Folders == 2 && File.ReadAllBytes(Path.Combine(root, "dir with space", "upfolder", "a.bin")).SequenceEqual(new byte[] { 0, 255, 10, 13, 39 }) && File.ReadAllText(Path.Combine(root, "dir with space", "upfolder", "sub dir", "b it.txt")) == "bee");
            Check(T("folder upload refuses existing target"), await Throws<IOException>(() => SafeOps.UploadFolderAsync(svc, src, X + "/dir with space/upfolder")));
            Check(T("folder upload into protected top level refused"), await Throws<InvalidOperationException>(() => SafeOps.UploadFolderAsync(svc, src, "/topfolder")));
            {
                Directory.CreateDirectory(Path.Combine(root, "dir with space", "repl")); File.WriteAllText(Path.Combine(root, "dir with space", "repl", "old.txt"), "old");
                string src2 = Path.Combine(work, tag + "-replsrc"); if (Directory.Exists(src2)) Directory.Delete(src2, true); Directory.CreateDirectory(src2); File.WriteAllText(Path.Combine(src2, "new.txt"), "new");
                var rr = await SafeOps.ReplaceFolderAsync(svc, src2, X + "/dir with space/repl");
                Check(T("folder replace: new content in place, old kept as .mibx-old"), File.Exists(Path.Combine(root, "dir with space", "repl", "new.txt")) && !File.Exists(Path.Combine(root, "dir with space", "repl", "old.txt")) && File.Exists(Path.Combine(root, "dir with space", "repl.mibx-old", "old.txt")) && !Directory.Exists(Path.Combine(root, "dir with space", "repl.mibx-new")));
                Check(T("folder replace refuses when backup name exists"), await Throws<IOException>(() => SafeOps.ReplaceFolderAsync(svc, src2, X + "/dir with space/repl")));
                Check(T("folder replace refuses missing folder"), await Throws<IOException>(() => SafeOps.ReplaceFolderAsync(svc, src2, X + "/dir with space/nonexist")));
                Directory.Delete(Path.Combine(root, "dir with space", "repl"), true); Directory.Delete(Path.Combine(root, "dir with space", "repl.mibx-old"), true);
            }
            Directory.Delete(Path.Combine(root, "dir with space", "upfolder"), true);
        }
        {
            var csvc = make(); await csvc.ConnectAsync(new ConnectionSettings { UsePrivateKey = false, Host = "127.0.0.1", Port = port, Username = user, Password = pw });
            string big = Path.Combine(work, tag + "-big.bin"); using (var fs = File.Create(big)) fs.SetLength(400L * 1024 * 1024);
            string dir = Path.Combine(root, "etc"); var before = Directory.GetFileSystemEntries(dir).Select(Path.GetFileName).OrderBy(x => x).ToList();
            var cts = new CancellationTokenSource(); cts.CancelAfter(30);
            bool cancelled = false; try { await SafeOps.UploadAsync(csvc, big, X + "/etc/cancel-up.bin", cts.Token); } catch (Exception) { cancelled = true; }
            Check(T("cancelled upload stops with an error"), cancelled);
            var after = Directory.GetFileSystemEntries(dir).Select(Path.GetFileName).OrderBy(x => x).ToList();
            var extra = after.Except(before).ToList(); Console.WriteLine("INFO " + tag + " leftovers after cancelled upload: " + (extra.Count == 0 ? "none" : string.Join(", ", extra)));
            foreach (var x in extra) { try { File.Delete(Path.Combine(dir, x)); } catch { } }
            if (!ftp)
            {
                byte[] orig = File.ReadAllBytes(Path.Combine(dir, "blob.bin")); var c2 = new CancellationTokenSource(); c2.CancelAfter(30); bool rc = false;
                try { await SafeOps.ReplaceAsync(csvc, big, X + "/etc/blob.bin", c2.Token); } catch (Exception) { rc = true; }
                var after2 = Directory.GetFileSystemEntries(dir).Select(Path.GetFileName).OrderBy(x => x).ToList(); var ex2 = after2.Except(before).ToList();
                Check(T("cancelled replace leaves the original file intact"), rc && File.ReadAllBytes(Path.Combine(dir, "blob.bin")).SequenceEqual(orig));
                Console.WriteLine("INFO " + tag + " leftovers after cancelled replace: " + (ex2.Count == 0 ? "none" : string.Join(", ", ex2)));
                foreach (var x in ex2) { try { File.Delete(Path.Combine(dir, x)); } catch { } }
            }
            bool alive = await csvc.ProbeConnectionAsync(TimeSpan.FromSeconds(3));
            Check(T("after a cancelled transfer the service reports its state honestly"), alive == csvc.IsConnected);
            Console.WriteLine("INFO " + tag + " connection after cancel: " + (alive ? "still usable" : "closed (app must offer reconnect)"));
            File.Delete(big);
        }
        {
            var upTop = (await svc.GetChildrenAsync("/")).Where(i => i.IsDirectory).ToList(); var upEtc = upTop.FirstOrDefault(i => i.Name == "etc");
            bool upNav = upEtc is not null && RemotePathNav.Parent(upEtc.FullPath) == "/";
            string upSeen = string.Join(",", upTop.Select(i => i.FullPath));
            if (upEtc is not null) { var upKids = await svc.GetChildrenAsync(upEtc.FullPath); upNav = upNav && upKids.Count > 0 && upKids.All(k => RemotePathNav.Parent(k.FullPath) == upEtc.FullPath); upSeen += " | upKids:" + string.Join(",", upKids.Select(k => k.FullPath)); }
            Check(T("Up: parent of every listed FullPath is the folder that was listed"), upNav, upSeen);
            Check(T("Up: edge cases"), RemotePathNav.Parent("/") == "/" && RemotePathNav.Parent("") == "/" && RemotePathNav.Parent(null) == "/" && RemotePathNav.Parent("/a") == "/" && RemotePathNav.Parent("/a/b/") == "/a" && RemotePathNav.Parent("//a//b") == "/a");
        }
        {
            // Create-only upload: never overwrite a file that exists at the last check, never leave this operation's temp behind, delete only the temp.
            string lp = Path.Combine(work, "upl_" + Guid.NewGuid().ToString("N")[..6] + ".txt"); File.WriteAllText(lp, "new-bytes");
            string foreign = Path.Combine(root, "etc", "up_foreign.txt"); File.WriteAllText(foreign, "FOREIGN");
            string exInfo = ""; bool threw = false; try { await SafeOps.UploadAsync(svc, lp, X + "/etc/up_foreign.txt"); } catch (Exception ux) { exInfo = ux.GetType().Name + ": " + ux.Message + " @ " + (ux.StackTrace ?? "").Split((char)10).Take(4).Aggregate("", (a, b) => a + b.Trim() + " | "); threw = ux is IOException; }
            bool keptForeign = File.ReadAllText(foreign) == "FOREIGN";
            bool noTemp = !Directory.GetFiles(Path.Combine(root, "etc"), ".mibx-up-*").Any() && !Directory.GetFiles(Path.Combine(root, "etc"), "*.mibx-part").Any();
            Check(T("upload: existing target is not overwritten, error raised, no temp left, foreign file untouched"), threw && keptForeign && noTemp, $"threw={threw} kept={keptForeign} noTemp={noTemp} ex={exInfo}");
            await SafeOps.UploadAsync(svc, lp, X + "/etc/up_new.txt");
            string created = Path.Combine(root, "etc", "up_new.txt");
            Check(T("upload: new target created with exact bytes, no temp left"), File.Exists(created) && File.ReadAllText(created) == "new-bytes" && !Directory.GetFiles(Path.Combine(root, "etc"), ".mibx-up-*").Any() && !Directory.GetFiles(Path.Combine(root, "etc"), "*.mibx-part").Any());
            File.Delete(foreign); File.Delete(created);
        }
        Check(T("probe"), await svc.ProbeConnectionAsync(TimeSpan.FromSeconds(3)));
        if (ftp)
        {
            Check(T("shell not supported (clear error)"), await Throws<NotSupportedException>(() => svc.ExecuteCommandAsync("ls")) && await Throws<NotSupportedException>(() => svc.CreateShellSessionAsync()));
        }
        else
        {
            Check(T("injection-safe path: name with ; and quote"), await InjectionAsync(svc, root, X));
            var sh = await svc.CreateShellSessionAsync(); var got = new System.Text.StringBuilder(); sh.TextReceived += (_, t) => got.Append(t);
            if (tag.Contains("SSH")) await sh.StartAsync(); await sh.SendCommandAsync("echo term-ok-$((20+22))"); await Task.Delay(800);
            Check(T("interactive terminal session works"), got.ToString().Contains("term-ok-42"), got.ToString()); sh.Dispose();
            if (tag.Contains("SSH"))
            {
                // Terminal lifecycle: shell exits -> Closed fires and input is refused; dispose -> input refused; a NEW shell on the same connection works; resize works while open.
                var s1 = await svc.CreateShellSessionAsync(); var o1 = new System.Text.StringBuilder(); s1.TextReceived += (_, t) => o1.Append(t); bool closed1 = false; s1.Closed += (_, _) => closed1 = true;
                await s1.StartAsync(); bool rz = true; string rzOut = "";
                await s1.SendCommandAsync("echo before-exit"); await Task.Delay(500); await s1.SendCommandAsync("exit"); for (int i = 0; i < 50 && !closed1; i++) await Task.Delay(100);
                { var s0 = await svc.CreateShellSessionAsync(); var o0 = new System.Text.StringBuilder(); s0.TextReceived += (_, t) => o0.Append(t); await s0.StartAsync(); try { await s0.ResizeAsync(120, 40); await Task.Delay(300); await s0.WriteInputAsync("echo after-resize-$((3*3))\r"); await Task.Delay(800); } catch { rz = false; } rzOut = o0.ToString().Replace("\n", "|").Replace("\r", "<CR>"); s0.Dispose(); }
                Check(T("terminal: resize accepted, input accepted after it, connection survives (the asyncssh fixture does not execute input after a window change, same with a Python reference client, so execution after resize is not asserted)"), rz, rzOut);
                Check(T("terminal: shell exit fires Closed"), closed1, "open=" + s1.IsOpen + " out=[" + o1.ToString().Replace("\n","|") + "]");
                bool refused = false; try { await s1.WriteInputAsync("x"); } catch { refused = true; }
                Check(T("terminal: input after the shell exited is refused (no silent send)"), refused);
                var s2 = await svc.CreateShellSessionAsync(); var o2 = new System.Text.StringBuilder(); s2.TextReceived += (_, t) => o2.Append(t); await s2.StartAsync(); await s2.SendCommandAsync("echo again-$((1+1))"); await Task.Delay(800);
                Check(T("terminal: a new shell on the same connection works after the first exited"), o2.ToString().Contains("again-2"), o2.ToString());
                s2.Dispose(); bool refused2 = false; try { await s2.WriteInputAsync("x"); } catch { refused2 = true; }
                Check(T("terminal: input after Dispose is refused"), refused2);
                s2.Dispose(); Check(T("terminal: double Dispose is safe"), true);
                var s3 = await svc.CreateShellSessionAsync(); await s3.StartAsync(); await s3.SendCommandAsync("echo alive"); await Task.Delay(300); s3.Dispose();
                Check(T("terminal: connection still usable after shells were disposed"), svc.IsConnected && (await svc.ExecuteCommandAsync("echo ok")).Contains("ok"));
                // Script pipeline: upload to /tmp, chmod, run, stream output, exit code, cleanup of the remote copy.
                string sd = Path.Combine(work, tag + "-script"); Directory.CreateDirectory(sd);
                string sp = Path.Combine(sd, "t_" + Guid.NewGuid().ToString("N")[..6] + ".sh");
                File.WriteAllText(sp, "#!/bin/sh\n# Type: ReadOnly\necho script-out-$((6*7))\necho err-line 1>&2\nexit 7\n");
                var item = new MibExplorer.Models.Scripting.ScriptItem { Name = "t", FileName = Path.GetFileName(sp), LocalPath = sp, ScriptType = "ReadOnly" };
                var outp = new System.Text.StringBuilder();
                var sres = await new MibExplorer.Services.Scripting.ScriptExecutionService(svc).ExecuteAsync(item, t => outp.Append(t).Append('\n'));
                Check(T("script run: output streamed, exit code 7 reported"), outp.ToString().Contains("script-out-42") && sres.ExitCode == 7, outp.ToString() + " exit=" + sres.ExitCode);
                Check(T("script run: remote copy removed from /tmp"), !File.Exists("/tmp/" + Path.GetFileName(sp)));
                var sres2 = await new MibExplorer.Services.Scripting.ScriptExecutionService(svc).ExecuteAsync(new MibExplorer.Models.Scripting.ScriptItem { Name = "x", FileName = "x.sh", LocalPath = "/nonexistent/x.sh" }, null);
                Check(T("script run: missing local file refused without touching the unit"), !sres2.Success && sres2.ExitCode == -1);
            }
        }
        await svc.DisconnectAsync();
        Check(T("disconnected"), !svc.IsConnected);
    }

    static async Task<bool> InjectionAsync(IMibConnectionService svc, string root, string X)
    {
        string name = "a;touch pwned;'b"; string src = Path.Combine(root, "etc", "hello.txt");
        await svc.CreateDirectoryWithoutMountAsync(X + "/" + name);
        bool ok = Directory.Exists(Path.Combine(root, name)) && !File.Exists(Path.Combine(root, "pwned")) && !File.Exists(Path.Combine(root, "etc", "pwned"));
        await svc.DeletePathWithoutMountAsync(X + "/" + name);
        return ok;
    }
}

public static class OneShot
{
    public static async Task<int> RunAsync(string[] a)
    {
        var s = new TelnetMibConnectionService();
        try
        {
            await s.ConnectAsync(new MibExplorer.Models.ConnectionSettings { Host = "127.0.0.1", Port = int.Parse(a[1]), Username = "root", Password = "secret" });
            Console.WriteLine("connected"); try { Console.WriteLine("OUT[" + (await s.ExecuteCommandAsync(a[2])).Replace("\n","\\n").Replace("\r","\\r") + "]"); } catch (Exception e2) { Console.WriteLine("CMDERR " + e2.Message.Replace("\n","\\n")); }
        }
        catch (Exception e) { Console.WriteLine("ERR " + e); }
        return 0;
    }
}
