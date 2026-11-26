using MibExplorer.Services;
public static class ConnectionHolderTests
{
    public static async Task<int> RunAsync()
    {
        int fail = 0; void Check(string n, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + n); if (!ok) fail++; }
        var h = new ConnectionHolder<object>(); var a = new object(); var b = new object();
        h.Current = a;
        Check("clear with the current connection clears it", h.ClearIfCurrent(a) && h.Current is null);
        h.Current = a;
        // old operation (bound to a) is still running; the user connects to b; then the old operation fails late.
        var gate = new TaskCompletionSource(); var late = Task.Run(async () => { await gate.Task; return h.ClearIfCurrent(a); });
        h.Current = b; gate.SetResult();
        Check("late failure of an old connection does not clear the new one", !await late && ReferenceEquals(h.Current, b));
        Check("clearing an unrelated connection when none is current is a no-op", !new ConnectionHolder<object>().ClearIfCurrent(a));
        // concurrent hammer: exactly one winner
        h.Current = a; int wins = 0; await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => { if (h.ClearIfCurrent(a)) Interlocked.Increment(ref wins); })));
        Check("50 concurrent clears: exactly one wins", wins == 1 && h.Current is null);
        return fail;
    }
}
