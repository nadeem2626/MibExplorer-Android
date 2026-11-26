namespace MibExplorer.Android;

// Shared glass brushes from Resources/Styles/Colors.xaml for buttons and cards built in code.
internal static class UiGlass
{
    private static Brush Get(string key) => Theme.B(key);
    public static Brush Primary => Get("GlassPrimary");
    public static Brush Secondary => Get("GlassSecondary");
    public static Brush Danger => Get("GlassDanger");
    public static Brush Card => Get("GlassCard");
    public static Brush Amber => new SolidColorBrush(Theme.C("Warn"));
}
