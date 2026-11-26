using System.Diagnostics;
public static class CodingScriptTests
{
    static int fail;
    static void Check(string n, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + n); if (!ok) fail++; }
    static (int code, string outp) Run(string script, string arg)
    {
        var psi = new ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(script); if (arg != null) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi)!; string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit(); return (p.ExitCode, o);
    }
    public static Task<int> RunAsync()
    {
        string dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../MibExplorer.Core/Payload/CodingCenter/Write"));
        string sc = Path.Combine(dir, "run.sh");
        var r = Run(sc, null!); Check("missing arg -> exit 1", r.code == 1 && r.outp.Contains("Missing target"));
        r = Run(sc, "ABCD"); Check("short -> exit 2", r.code == 2 && r.outp.Contains("Invalid target coding length"));
        r = Run(sc, new string('A', 51)); Check("long -> exit 2", r.code == 2);
        r = Run(sc, new string('G', 50)); Check("50 non-hex -> exit 2 hex-only", r.code == 2 && r.outp.Contains("hex digits only"));
        string inj = ";touch /tmp/mibx_inj_marker;" + new string('A', 50 - 27); if (File.Exists("/tmp/mibx_inj_marker")) File.Delete("/tmp/mibx_inj_marker");
        r = Run(sc, inj.PadRight(50, 'A').Substring(0, 50)); Check("metacharacters rejected, nothing executed", r.code == 2 && !File.Exists("/tmp/mibx_inj_marker"));
        r = Run(sc, "$(id)" + new string('A', 45)); Check("command substitution text rejected", r.code == 2);
        var svc = new MibExplorer.Services.Coding.CodingCenterService(null!);
        try { svc.Write5FCodingAsync("zz").GetAwaiter().GetResult(); Check("C# guard rejects bad hex", false); } catch (ArgumentException) { Check("C# guard rejects bad hex before any connection use", true); } catch (Exception e) { Check("C# guard: unexpected " + e.GetType().Name, false); }
        return Task.FromResult(fail);
    }
}
