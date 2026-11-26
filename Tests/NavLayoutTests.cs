using MibExplorer.Services;
public static class NavLayoutTests
{
    public static int Run()
    {
        int fail = 0; void Check(string n, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + n); if (!ok) fail++; }
        Check("tablet landscape 1280x800 uses the side rail", NavLayout.Choose(1280, 800) == NavMode.SideRail);
        Check("tablet portrait 800x1280 keeps bottom tabs", NavLayout.Choose(800, 1280) == NavMode.BottomTabs);
        Check("phone portrait 412x915 keeps bottom tabs", NavLayout.Choose(412, 915) == NavMode.BottomTabs);
        Check("phone landscape 915x412 uses the side rail", NavLayout.Choose(915, 412) == NavMode.SideRail);
        Check("small landscape 560x320 keeps bottom tabs", NavLayout.Choose(560, 320) == NavMode.BottomTabs);
        Check("square and zero sizes keep bottom tabs (no flip on startup)", NavLayout.Choose(800, 800) == NavMode.BottomTabs && NavLayout.Choose(0, 0) == NavMode.BottomTabs && NavLayout.Choose(-1, 5) == NavMode.BottomTabs);
        Check("rotation round trip is stable", NavLayout.Choose(1280, 800) == NavMode.SideRail && NavLayout.Choose(800, 1280) == NavMode.BottomTabs && NavLayout.Choose(1280, 800) == NavMode.SideRail);
        Check("same destinations for both layouts, Terminal not a destination", string.Join(",", NavLayout.Items.Select(i => i.Title)) == "Connect,Files,Coding,Adaptations,Tools,Settings" && NavLayout.Items.Count(i => !i.IsPushedPage) == 5);
        Check("route of shell location", NavLayout.RouteOf("//Files") == "Files" && NavLayout.RouteOf("//Coding/x") == "Coding" && NavLayout.RouteOf("//Terminal") is null && NavLayout.RouteOf("//Nope") is null && NavLayout.RouteOf(null) is null && NavLayout.RouteOf("//Transfers") is null);
        Check("Modified column: shown on tablet widths (also landscape page 1148 and portrait 800), hidden on phone portrait 412", RowLayout.ShowModified(1148) && RowLayout.ShowModified(800) && !RowLayout.ShowModified(412) && RowLayout.ShowModified(480) && !RowLayout.ShowModified(479));
        Check("Modified column: unknown width before the first pass keeps the design layout", RowLayout.ShowModified(0));
        return fail;
    }
}
