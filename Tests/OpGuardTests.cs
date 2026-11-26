using MibExplorer.Services;

public static class OpGuardTests
{
    sealed class Svc { public string Name = ""; }
    public static async Task<int> RunAsync()
    {
        int fail = 0; void Check(string n, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + n); if (!ok) fail++; }
        Svc? cur = new Svc { Name = "A" }; bool write = true; Svc? ran = null; int runs = 0;
        Task Act(Svc s) { ran = s; runs++; return Task.CompletedTask; }

        var r = await ConfirmedAction.RunAsync(() => cur, () => write, () => Task.FromResult(true), Act);
        Check("same owner, still allowed: runs once with the captured owner", r == GuardResult.Done && runs == 1 && ReferenceEquals(ran, cur));

        var a = cur; runs = 0; ran = null;
        r = await ConfirmedAction.RunAsync(() => cur, () => write, () => { cur = new Svc { Name = "B" }; return Task.FromResult(true); }, Act);
        Check("owner replaced during the confirm dialog: nothing runs", r == GuardResult.OwnerChanged && runs == 0 && ran is null);

        cur = new Svc { Name = "C" }; write = true; runs = 0;
        r = await ConfirmedAction.RunAsync(() => cur, () => write, () => { write = false; return Task.FromResult(true); }, Act);
        Check("write mode turned off during the confirm dialog: nothing runs", r == GuardResult.NotAllowed && runs == 0);

        write = true; runs = 0;
        r = await ConfirmedAction.RunAsync(() => cur, () => write, () => Task.FromResult(false), Act);
        Check("declined: nothing runs", r == GuardResult.Declined && runs == 0);

        cur = null; bool asked = false;
        r = await ConfirmedAction.RunAsync(() => cur, () => write, () => { asked = true; return Task.FromResult(true); }, Act);
        Check("no connection: refused before the dialog is even shown", r == GuardResult.NotAllowed && !asked && runs == 0);

        var o = new Svc(); var other = new Svc(); Svc? curA = o; runs = 0; ran = null;
        r = await ConfirmedAction.RunAsync(() => curA, () => true, () => { curA = other; curA = o; return Task.FromResult(true); }, Act);
        Check("owner swapped and swapped back is the same object: allowed", r == GuardResult.Done && ReferenceEquals(ran, o));
        Check("describe texts say nothing was changed", ConfirmedAction.Describe(GuardResult.OwnerChanged, "written").Contains("Nothing was written") && ConfirmedAction.Describe(GuardResult.Done, "written") == "");

        // validation order
        var gate = new ConnectGate(); int tear = 0, conn = 0;
        string? err = await gate.RunAsync(() => ConnectGate.ValidateInput("99999", "10.0.0.1", "root", true, out _), () => tear++, () => { conn++; return Task.CompletedTask; });
        Check("bad port: error returned, working connection NOT torn down, connect not run", err != null && tear == 0 && conn == 0 && !gate.Busy);
        err = await gate.RunAsync(() => ConnectGate.ValidateInput("22", " ", "root", true, out _), () => tear++, () => { conn++; return Task.CompletedTask; });
        Check("empty host: refused before teardown", err == "Enter the unit IP" && tear == 0);
        err = await gate.RunAsync(() => ConnectGate.ValidateInput("21", "10.0.0.1", "", false, out _), () => tear++, () => { conn++; return Task.CompletedTask; });
        Check("FTP/Telnet without user: refused before teardown; SSH without user is allowed", err == "Enter a user name" && tear == 0 && ConnectGate.ValidateInput("22", "h", "", true, out int p22) == null && p22 == 22);
        err = await gate.RunAsync(() => null, () => tear++, () => { conn++; return Task.CompletedTask; });
        Check("valid input: teardown then connect, gate released", err == null && tear == 1 && conn == 1 && !gate.Busy);

        var tcs = new TaskCompletionSource();
        var first = gate.RunAsync(() => null, () => tear++, () => tcs.Task);
        string? second = await gate.RunAsync(() => null, () => tear++, () => { conn++; return Task.CompletedTask; });
        Check("second connect while one is running: refused, no extra teardown", second == "busy" && tear == 2 && conn == 1 && gate.Busy);
        tcs.SetResult(); await first;
        Check("gate released after the first finishes", !gate.Busy);
        try { await gate.RunAsync(() => null, () => { }, () => throw new InvalidOperationException("x")); Check("exception propagates", false); }
        catch (InvalidOperationException) { Check("connect exception propagates and the gate is released", !gate.Busy); }
        return fail == 0 ? 0 : 1;
    }
}
