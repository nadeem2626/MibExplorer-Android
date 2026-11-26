namespace MibExplorer.Android;

public partial class LicensesPage : ContentPage
{
    public LicensesPage()
    {
        InitializeComponent();
        Load();
    }

    private async void Load()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var n in new[] { "terminal/vendor/xterm.LICENSE", "terminal/vendor/addon-fit.LICENSE" })
        {
            try { using var s = await FileSystem.OpenAppPackageFileAsync(n); using var r = new StreamReader(s); sb.AppendLine("== " + Path.GetFileName(n) + " ==").AppendLine(await r.ReadToEndAsync()).AppendLine(); }
            catch { sb.AppendLine("== " + Path.GetFileName(n) + " == (license file not bundled in this build)").AppendLine(); }
        }
        Third.Text = sb.ToString();
    }
}
