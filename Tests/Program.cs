using MibExplorer.Services;
using Renci.SshNet;
if (args.Length > 0 && args[0] == "proto") return await ProtocolTests.RunAsync(args);
if (args.Length > 0 && args[0] == "one") return await OneShot.RunAsync(args);
if (args.Length > 0 && args[0] == "coding") return await CodingTests.RunAsync();
if (args.Length > 0 && args[0] == "codingwrite") return await CodingWriteTests.RunAsync();
if (args.Length > 0 && args[0] == "sshone") return await SshOne.RunAsync(args);
if (args.Length > 0 && args[0] == "terminallog") return await TerminalLogTests.RunAsync();
if (args.Length > 0 && args[0] == "packages") return await PackageTests.RunAsync();
if (args.Length > 0 && args[0] == "editing") return await EditingTests.RunAsync();
if (args.Length > 0 && args[0] == "connholder") return await ConnectionHolderTests.RunAsync();
if (args.Length > 0 && args[0] == "details") return FileDetailsTests.Run();
if (args.Length > 0 && args[0] == "filelist") return FileListViewTests.Run();
if (args.Length > 0 && args[0] == "lsdate") return LsDateTests.Run();
if (args.Length > 0 && args[0] == "navlayout") return NavLayoutTests.Run();
if (args.Length > 0 && args[0] == "navstate") return await NavStateTests.RunAsync();
if (args.Length > 0 && args[0] == "uninstall") return await UninstallTests.RunAsync();
if (args.Length > 0 && args[0] == "keypaste") return await KeyPasteTests.RunAsync();
if (args.Length > 0 && args[0] == "opguards") return await OpGuardTests.RunAsync();
if (args.Length > 0 && args[0] == "officialscripts") return await OfficialScriptsTests.RunAsync(args);
if (args.Length > 0 && args[0] == "seed") return await SeedTests.RunAsync();
if (args.Length > 0 && args[0] == "atomicfile") return await AtomicFileTests.RunAsync();
if (args.Length > 0 && args[0] == "downloadcancel") return await DownloadCancelTests.RunAsync(args);
if (args.Length > 0 && args[0] == "codingscript") return await CodingScriptTests.RunAsync();
if (args.Length > 0 && args[0] == "cancelloop") return await CancelLoopTests.RunAsync(args);
if (args.Length > 0 && args[0] == "adaptreal") return await AdaptRealTests.RunAsync();
if (args.Length > 0 && args[0] == "hostkeys") return HostKeyTests.Run();
if (args.Length > 0 && args[0] == "namemap") return await NameMapTests.RunAsync();
if (args.Length > 0 && args[0] == "queue") return await QueueTests.RunAsync();
if (args.Length > 0 && args[0] == "reboot") return await RebootTests.RunAsync();
if (args.Length > 0 && args[0] == "adaptations") return await AdaptationTests.RunAsync();
var dir = Path.Combine(Path.GetTempPath(), "keytest" + Guid.NewGuid());
var r = await new SshKeyService().GenerateRsaKeyPairAsync(dir, "id_rsa", "test@mib");
using var key = new PrivateKeyFile(r.PrivateKeyPath);
Console.WriteLine("SSH.NET loaded key: " + key.HostKeyAlgorithms.First().Name);
var tmp = Path.Combine(dir, "ref");
var psi = new System.Diagnostics.ProcessStartInfo("ssh-keygen", $"-y -f {r.PrivateKeyPath}") { RedirectStandardOutput = true };
var o = System.Diagnostics.Process.Start(psi)!; var outp = o.StandardOutput.ReadToEnd().Trim(); o.WaitForExit();
bool match = outp.Split(' ')[1] == r.PublicKeyOpenSsh.Split(' ')[1];
Console.WriteLine("pubkey matches ssh-keygen derivation: " + match);


// Host key pinning logic
var store = Path.Combine(dir, "known.json");
var v = new MibExplorer.Services.HostKeyVerifier(store);
bool rejNew = !v.Verify("h:22","ssh-rsa","SHA256:A");               // no confirm callback -> reject
v.ConfirmNewKey = (_, _, _) => true;
bool acceptFirst = v.Verify("h:22","ssh-rsa","SHA256:A");           // confirmed -> pinned
var v2 = new MibExplorer.Services.HostKeyVerifier(store);            // reload from disk
bool trusted = v2.Verify("h:22","ssh-rsa","SHA256:A");
bool changed = !v2.Verify("h:22","ssh-rsa","SHA256:B");              // changed key rejected even with confirm=true
Console.WriteLine($"hostkey: rejectUnconfirmed={rejNew} acceptFirst={acceptFirst} trustedAfterReload={trusted} changedRejected={changed}");
return (match && rejNew && acceptFirst && trusted && changed) ? 0 : 1;
