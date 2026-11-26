namespace MibExplorer.Android;

/// <summary>Responsive policy for list + detail pages. Side: two panes. Stacked: detail under the list, only when the page is tall enough
/// that the list keeps most of the height. Drill: narrow AND short, so the list uses the whole area and the detail replaces it on demand (with a Back button).
/// Thresholds are page dp sizes, not device guesses. Unverified on a device.</summary>
public enum PaneMode { Side, Stacked, Drill }

public static class PaneLayout
{
    public static PaneMode Choose(double width, double height) => width >= 800 ? PaneMode.Side : height >= 700 ? PaneMode.Stacked : PaneMode.Drill;
    public static double StackedHeight(double height) => Math.Max(160, Math.Min(260, height * 0.33));
}
