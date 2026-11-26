using MibExplorer.Models; using MibExplorer.Services;
if (args[0]=="memkey"){
  var before = Directory.GetFiles(Path.GetTempPath()).Length; var cwdBefore = Directory.GetFiles(".").Length;
  var (pem,pub) = SshKeyService.GenerateInMemory("t@mib");
  var after = Directory.GetFiles(Path.GetTempPath()).Length; var cwdAfter = Directory.GetFiles(".").Length;
  using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(pem)); using var pk = new Renci.SshNet.PrivateKeyFile(ms);
  var f = Path.GetTempFileName(); File.WriteAllText(f, pem); System.Diagnostics.Process.Start("chmod", $"600 {f}")!.WaitForExit();
  var psi = new System.Diagnostics.ProcessStartInfo("ssh-keygen", $"-y -f {f}"){RedirectStandardOutput=true}; var pr=System.Diagnostics.Process.Start(psi)!; var o=pr.StandardOutput.ReadToEnd().Trim(); pr.WaitForExit(); File.Delete(f);
  Console.WriteLine($"memkey: noFilesWritten={before+0==after-0 && cwdBefore==cwdAfter} loadsInSshNet=True pubMatchesSshKeygen={o.Split(' ')[1]==pub.Split(' ')[1]}"); return; }
if (args[0]=="genkey"){ var r=await new SshKeyService().GenerateRsaKeyPairAsync("/tmp/srv/ck","id","t@mib"); File.Copy(r.PublicKeyPath,"/tmp/srv/auth.pub",true); Console.WriteLine("genkey ok"); return; }
string port = args[0]; string store = args[1]; bool confirm = args[2]=="confirm";
var cs = new ConnectionSettings{Host="127.0.0.1",Port=int.Parse(port),Username="u",Password="pw",UsePrivateKey=false}; if(args.Length>3 && args[3]=="pem"){ cs.UsePrivateKey=true; cs.PrivateKeyPem=File.ReadAllText("/tmp/srv/ck/id"); cs.Password=""; };
var svc = new SshMibConnectionService{ HostKeys = new HostKeyVerifier(store){ ConfirmNewKey=(h,a,f)=>{Console.WriteLine($"confirm? {h} {a} {f}"); return confirm;} } };
try {
  await svc.ConnectAsync(cs);
  var items = await svc.GetChildrenAsync("/tmp/srv/root");
  Console.WriteLine("list: " + string.Join(",", items.Select(i=>i.Name+(i.IsDirectory?"/":""))));
  var o = await svc.ExecuteCommandAsync("echo hi"); Console.WriteLine("exec: "+o.Trim());
  var local = Path.Combine(Path.GetTempPath(),"dl_hello.txt"); if(File.Exists(local)) File.Delete(local);
  await svc.DownloadFileAsync("/hello.txt", local);
  Console.WriteLine("download: " + File.ReadAllText(local).Trim());
  Console.WriteLine("RESULT connected");
} catch (Exception e) { Console.WriteLine("RESULT failed: " + e.GetType().Name + ": " + e.Message); }
// Required-pinning guard: no verifier + required => refuse before any network use
try { await new SshMibConnectionService{ RequireHostKeyPinning = true }.ConnectAsync(cs); Console.WriteLine("GUARD failed: connected unverified"); }
catch (InvalidOperationException) { Console.WriteLine("GUARD ok: refused without verifier"); }
