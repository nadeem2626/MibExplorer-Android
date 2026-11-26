using MibExplorer.Services;
public static class NavStateTests
{
    public static async Task<int> RunAsync()
    {
        int fail = 0; void Check(string n, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + n); if (!ok) fail++; }
        var a = new object(); var b = new object();
        var n = new ListNavigator<object>();
        Check("fresh: Up from root is refused", n.UpTarget() is null);
        var t1 = n.Begin("/a/b", a); Check("complete current: accepted, displayed set, pending cleared", n.Complete(t1, a) && n.Displayed == "/a/b" && n.Pending is null);
        Check("Up from displayed", n.UpTarget() == "/a");
        // failed navigation resets pending; Up starts from what is displayed
        var t2 = n.Begin("/a/b/c", a); Check("Up while opening starts from the pending path", n.UpTarget() == "/a/b");
        Check("failure of current listing accepted, pending cleared, displayed unchanged", n.Fail(t2) && n.Pending is null && n.Displayed == "/a/b");
        Check("Up after failed navigation starts from the displayed folder", n.UpTarget() == "/a");
        // overlapping listings: older slow result must not change anything
        var slow = n.Begin("/x", a); var fast = n.Begin("/a", a);
        Check("newer completes first", n.Complete(fast, a) && n.Displayed == "/a");
        Check("older slow result is dropped, displayed unchanged", !n.Complete(slow, a) && n.Displayed == "/a" && n.Pending is null);
        var s2 = n.Begin("/y", a); var f2 = n.Begin("/z", a);
        Check("stale failure does not clear the newer pending path", !n.Fail(s2) && n.Pending == "/z");
        Check("Up pressed twice during a slow listing steps up from the pending path once per call", n.UpTarget() == "/");
        n.Complete(f2, a);
        // connection switch
        var c1 = n.Begin("/q/r", a); n.Reset(); var tb = n.Begin("/", b);
        Check("result of the old connection after reset is dropped", !n.Complete(c1, b) && n.Displayed is null);
        Check("current listing on new connection accepted", n.Complete(tb, b) && n.Displayed == "/" && ReferenceEquals(n.DisplayedConn, b));
        var late = n.Begin("/k", a); // same generation, but connection a is no longer current
        Check("current-generation result from a connection that is no longer current is dropped and clears pending", !n.Complete(late, b) && n.Pending is null && n.Displayed == "/");
        Check("Up at top says nothing to do", n.UpTarget() is null);
        // concurrent hammer: exactly one of many overlapping listings may win
        int wins = 0; var tickets = Enumerable.Range(0, 200).Select(i => n.Begin("/p" + i, b)).ToList();
        await Task.WhenAll(tickets.Select(t => Task.Run(() => { if (n.Complete(t, b)) Interlocked.Increment(ref wins); })));
        Check("200 overlapping listings: only the newest is accepted", wins == 1 && n.Displayed == "/p199");
        // selection
        var sel = new ListingSelection<object, string>(); sel.Bind(a, "/d");
        Check("toggle marks and unmarks", sel.Toggle("f1") && !sel.Toggle("f1") && sel.Count == 0);
        sel.Toggle("f1"); sel.Toggle("f2");
        Check("take with same connection and folder", sel.Take(a, "/d")!.Count == 2);
        Check("take refused for another connection", sel.Take(b, "/d") is null);
        Check("take refused for another folder", sel.Take(a, "/e") is null);
        sel.Bind(a, "/e"); Check("a new listing drops all marks", sel.Count == 0 && sel.Take(a, "/e")!.Count == 0);
        return fail;
    }
}
