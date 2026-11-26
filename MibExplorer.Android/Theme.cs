using MibExplorer.Android.Themes;
namespace MibExplorer.Android;

/// <summary>Implemented by pages that keep colours in data (for example list rows) and must refresh them when the theme changes.</summary>
public interface IThemeAware { void OnThemeChanged(); }

/// <summary>Day / Night colours. The saved choice is "night" (the previous look, default), "day" or "system" (follows the Device setting).
/// A switch is done in place: no page is recreated, no event handler is added, Session and Transfers are not touched.</summary>
internal static class Theme
{
    public const string PrefKey = "theme_mode";
    public static string Mode => Preferences.Get(PrefKey, "night");
    public static bool IsDay { get; private set; }
    private static bool _applied;

    public static Color C(string key) => Application.Current is { } a && a.Resources.TryGetValue(key, out var v) && v is Color c ? c : Colors.Magenta; // magenta = missing key, easy to notice
    public static Brush B(string key) => Application.Current is { } a && a.Resources.TryGetValue(key, out var v) && v is Brush b ? b : new SolidColorBrush(Colors.Magenta);
    public static string Logo => IsDay ? "logo_mib_day.png" : "logo_mib.png";
    public static string Icon(string png) => IsDay ? png.Replace(".png", "_day.png") : png;

    /// <summary>Saves a mode and applies it to everything that is open right now.</summary>
    public static void SetMode(string mode) { Preferences.Set(PrefKey, mode); Apply(); }

    /// <summary>Applies the saved mode. Returns true when the effective look (day or night) changed.</summary>
    public static bool Apply()
    {
        var app = Application.Current; if (app is null) return false;
        string m = Mode;
        app.UserAppTheme = m == "day" ? AppTheme.Light : m == "night" ? AppTheme.Dark : AppTheme.Unspecified;
        bool day = m == "day" || (m == "system" && app.RequestedTheme == AppTheme.Light);
        if (_applied && day == IsDay) return false;
        bool first = !_applied;
        ResourceDictionary next = day ? new DayColors() : new NightColors();

        // 1. Old and new values per key, taken before anything changes.
        var colorMap = new Dictionary<Color, Color>(); var brushMap = new Dictionary<Brush, Brush>();
        if (!first)
            foreach (var kv in next)
            {
                if (kv.Value is Color nc && app.Resources.TryGetValue(kv.Key, out var oc) && oc is Color oldC && oldC != nc) colorMap.TryAdd(oldC, nc);
                else if (kv.Value is Brush nb && app.Resources.TryGetValue(kv.Key, out var ob) && ob is Brush oldB && !ReferenceEquals(oldB, nb)) brushMap.TryAdd(oldB, nb);
            }

        // 2. New values go into the app's own dictionary, which wins over the merged defaults. Every DynamicResource in XAML updates from this.
        foreach (var kv in next) app.Resources[kv.Key] = kv.Value;
        IsDay = day; _applied = true;
        if (first) return true;

        // 3. Values that code set once (labels, buttons, borders built in code) still hold the old colour: map them over. Values bound to DynamicResource already have the new colour and are left alone.
        try
        {
            foreach (var page in OpenPages()) { Walk(page, colorMap, brushMap); if (page is IThemeAware ta) ta.OnThemeChanged(); }
            if (Shell.Current is AppShell sh) sh.ApplyTheme();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("theme apply: " + ex.Message); }
        return true;
    }

    private static IEnumerable<Page> OpenPages()
    {
        var seen = new HashSet<Page>();
        var shell = Shell.Current; if (shell is null) yield break;
        foreach (var item in shell.Items) foreach (var section in item.Items)
        {
            foreach (var content in section.Items) if (((IShellContentController)content).Page is { } p && seen.Add(p)) yield return p;
            foreach (var p in section.Navigation.NavigationStack) if (p is not null && seen.Add(p)) yield return p;
        }
        foreach (var p in shell.Navigation.NavigationStack) if (p is not null && seen.Add(p)) yield return p;
        foreach (var p in shell.Navigation.ModalStack) if (p is not null && seen.Add(p)) yield return p;
    }

    private static Color? Map(Color? c, Dictionary<Color, Color> m) => c is { } v && m.TryGetValue(v, out var n) ? n : null;

    private static void Walk(IVisualTreeElement el, Dictionary<Color, Color> cm, Dictionary<Brush, Brush> bm)
    {
        if (el is VisualElement ve)
        {
            if (ve.Background is { } bg)
            {
                if (bm.TryGetValue(bg, out var nb)) ve.Background = nb;
                else if (bg is SolidColorBrush sb && Map(sb.Color, cm) is { } nc) ve.Background = new SolidColorBrush(nc);
            }
        }
        switch (el)
        {
            case Label l: if (Map(l.TextColor, cm) is { } a) l.TextColor = a; break;
            case Button b: if (Map(b.TextColor, cm) is { } a2) b.TextColor = a2; break;
            case InputView iv: if (Map(iv.TextColor, cm) is { } a3) iv.TextColor = a3; if (Map(iv.PlaceholderColor, cm) is { } a4) iv.PlaceholderColor = a4; break;
            case Picker p: if (Map(p.TextColor, cm) is { } a5) p.TextColor = a5; if (Map(p.TitleColor, cm) is { } a6) p.TitleColor = a6; break;
            case ProgressBar pb: if (Map(pb.ProgressColor, cm) is { } a7) pb.ProgressColor = a7; break;
            case Switch sw: if (Map(sw.OnColor, cm) is { } a8) sw.OnColor = a8; break;
            case BoxView bv: if (Map(bv.Color, cm) is { } a9) bv.Color = a9; break;
            case CheckBox cb: if (Map(cb.Color, cm) is { } a10) cb.Color = a10; break;
            case Border bo: if (bo.Stroke is { } st) { if (bm.TryGetValue(st, out var ns)) bo.Stroke = ns; else if (st is SolidColorBrush ssb && Map(ssb.Color, cm) is { } sc) bo.Stroke = new SolidColorBrush(sc); } break;
        }
        if (el is VisualElement v2 && v2.BackgroundColor is { } bc && Map(bc, cm) is { } nbc) v2.BackgroundColor = nbc;
        foreach (var child in el.GetVisualChildren()) Walk(child, cm, bm);
    }
}
