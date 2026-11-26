using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MibExplorer.Services;

public static class PackageTests
{
    static int pass, fail;
    static void Check(string n, bool ok, string? d = null) { if (ok) { pass++; Console.WriteLine("PASS " + n); } else { fail++; Console.WriteLine("FAIL " + n + (d == null ? "" : " -> " + d)); } }
    static string Sha1(byte[] b) => Convert.ToHexString(SHA1.HashData(b)).ToLowerInvariant();

    public static Task<int> RunAsync()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "mibx-pkg-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp);
        SdUpdatePackageBuilder.OutputRoot = tmp; SdUninstallPackageBuilder.OutputRoot = tmp;
        foreach (bool install in new[] { true, false })
        {
            string name = install ? "installer" : "uninstaller";
            string zip = install ? new SdUpdatePackageBuilder().BuildPackage() : new SdUninstallPackageBuilder().BuildPackage();
            Check(name + ": zip written under the writable root", File.Exists(zip) && zip.StartsWith(tmp));
            using var z = ZipFile.OpenRead(zip);
            string? E(string n) => z.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith(n))?.FullName;
            byte[] Read(string n) { var e = z.GetEntry(E(n)!)!; using var s = e.Open(); using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray(); }
            Check(name + ": has Swdlautorun, metainfo2, finalScript, hashes, scripts", new[] { "Swdlautorun.txt", "metainfo2.txt", "final/finalScript.sh", "final/hashes.txt", "scripts/finish_ssh_boot.sh" }.All(n => E(n) != null));
            string meta = System.Text.Encoding.UTF8.GetString(Read("metainfo2.txt"));
            var fs = Read("final/finalScript.sh"); var hs = Read("final/hashes.txt");
            Check(name + ": metainfo FinalScriptChecksum = SHA1(finalScript.sh)", meta.Contains($"FinalScriptChecksum = \"{Sha1(fs)}\""));
            Check(name + ": hashes.txt lists finalScript.sh with right size and SHA1", System.Text.Encoding.UTF8.GetString(hs).Contains($"CheckSum = \"{Sha1(fs)}\"") && System.Text.Encoding.UTF8.GetString(hs).Contains($"FileSize = \"{fs.Length}\""));
            Check(name + ": metainfo records SHA1 and size of hashes.txt", meta.Contains($"CheckSum = \"{Sha1(hs)}\"") && meta.Contains($"FileSize = \"{hs.Length}\""));
            Check(name + ": CRLF in metainfo and hashes, LF only in shell scripts", meta.Contains("\r\n") && System.Text.Encoding.UTF8.GetString(hs).Contains("\r\n") && !fs.Contains((byte)13));
            if (install)
            {
                var pub = Read("GEM/id_rsa.pub");
                Check("installer: public key SHA1 and size match metainfo", meta.Contains($"CheckSum = \"{Sha1(pub)}\"") && meta.Contains($"FileSize = \"{pub.Length}\""));
                string pubText = System.Text.Encoding.UTF8.GetString(pub);
                Check("installer: public key is ssh-rsa", pubText.StartsWith("ssh-rsa "), pubText[..Math.Min(20, pubText.Length)]);
                Check("installer: private key kept in writable root, not in the zip", File.Exists(Path.Combine(tmp, "Keys", "id_rsa")) && !z.Entries.Any(e => e.FullName.Contains("id_rsa") && !e.FullName.EndsWith(".pub")));
                Check("installer: sshd payload extracted", z.Entries.Any(e => e.FullName.Replace('\\', '/').Contains("GEM/sshd/") && e.Length > 0));
                string key1 = File.ReadAllText(Path.Combine(tmp, "Keys", "id_rsa"));
                new SdUpdatePackageBuilder().BuildPackage();
                Check("installer: second build reuses the same key", File.ReadAllText(Path.Combine(tmp, "Keys", "id_rsa")) == key1);
            }
            else Check("uninstaller: no key material in the package", !z.Entries.Any(e => e.FullName.Contains("id_rsa")));
            // shell syntax of every .sh inside
            int bad = 0, n = 0;
            foreach (var e in z.Entries.Where(e => e.FullName.EndsWith(".sh")))
            {
                string f = Path.Combine(tmp, "chk_" + (n++) + ".sh"); using (var s = e.Open()) using (var o = File.Create(f)) s.CopyTo(o);
                var p = Process.Start(new ProcessStartInfo("sh", new[] { "-n", f }) { RedirectStandardError = true })!; p.WaitForExit(); if (p.ExitCode != 0) { bad++; Console.WriteLine(p.StandardError.ReadToEnd()); }
            }
            Check(name + $": {n} shell scripts pass sh -n", n >= 2 && bad == 0);
        }
        {
            var (pem, pub) = MibExplorer.Services.SshKeyService.GenerateInMemory("x");
            string tmp2 = Path.Combine(tmp, "ov"); Directory.CreateDirectory(tmp2); SdUpdatePackageBuilder.OutputRoot = tmp2; SdUpdatePackageBuilder.PrivateKeyPemOverride = pem;
            string zip2 = new SdUpdatePackageBuilder().BuildPackage(); SdUpdatePackageBuilder.PrivateKeyPemOverride = null;
            using var z2 = ZipFile.OpenRead(zip2); var pe = z2.Entries.First(e => e.FullName.EndsWith("id_rsa.pub")); using var rs = new StreamReader(pe.Open());
            string got = rs.ReadToEnd().Trim();
            Check("override key: package public key equals the key the app connects with", got.Split(' ')[1] == pub.Trim().Split(' ')[1], got);
            Check("override key: no Keys folder or private key file created", !Directory.Exists(Path.Combine(tmp2, "Keys")));
        }
        Directory.Delete(tmp, true);
        Console.WriteLine($"packages: {pass} pass, {fail} fail");
        return Task.FromResult(fail == 0 ? 0 : 1);
    }
}
