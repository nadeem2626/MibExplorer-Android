using MibExplorer.Services.Editing;
public static class TerminalLogTests
{
    static int pass, fail;
    static void Check(string n, bool ok, string? d = null) { if (ok) { pass++; Console.WriteLine("PASS " + n); } else { fail++; Console.WriteLine("FAIL " + n + " -> " + d); } }
    public static Task<int> RunAsync()
    {
        var l = new TerminalLog(); l.Append("\u001b[32mroot@mib\u001b[0m:~# ls\r\n\u001b[1;34mdir\u001b[0m  file\r\n"); 
        Check("colors stripped, CRLF to LF", l.ToString() == "root@mib:~# ls\ndir  file\n", l.ToString());
        l = new TerminalLog(); l.Append("abc\b\bX\r\n"); Check("backspace applied", l.ToString() == "aX\n", l.ToString());
        l = new TerminalLog(); l.Append("\u001b]0;title\u0007hello\u001b[2J\u001b[H\n"); Check("OSC title and clear stripped", l.ToString() == "hello\n", l.ToString());
        l = new TerminalLog(1000); for (int i = 0; i < 500; i++) l.Append("line " + i + "\n"); Check("bounded size, keeps the tail", l.ToString().Length <= 1000 && l.ToString().Contains("line 499"));
        l = new TerminalLog(); l.Append("ok\u0007\u0000end"); Check("control chars dropped", l.ToString() == "okend", l.ToString());
        Console.WriteLine($"terminallog: {pass} pass, {fail} fail"); return Task.FromResult(fail == 0 ? 0 : 1);
    }
}
