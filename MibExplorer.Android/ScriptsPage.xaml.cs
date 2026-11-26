using MibExplorer.Services;
using System.Text;
using MibExplorer.Models.Scripting;
using MibExplorer.Services.Scripting;
using MibExplorer.Settings;

namespace MibExplorer.Android;

public partial class ScriptsPage : ContentPage
{
    private readonly ScriptCatalogService _catalog = new();
    private readonly OfficialScriptUpdateService _updater = new();
    private CancellationTokenSource? _cts;
    private readonly StringBuilder _out = new();

    private double _w, _h; private bool _showOut;
    protected override void OnSizeAllocated(double width, double height) { base.OnSizeAllocated(width, height); if (width > 0) _w = width; if (height > 0) _h = height; ApplyLayout(); }
    private void ApplyLayout()
    {
        if (_w <= 0) return;
        switch (PaneLayout.Choose(_w, _h))
        {
            case PaneMode.Side:
                List.IsVisible = true; OutCard.IsVisible = true; OutToggle.IsVisible = false; Grid.SetColumnSpan(List, 1);
                Grid.SetRow(OutCard, 2); Grid.SetColumn(OutCard, 1); Grid.SetColumnSpan(OutCard, 1); OutCard.HeightRequest = -1; break;
            case PaneMode.Stacked:
                List.IsVisible = true; OutCard.IsVisible = true; OutToggle.IsVisible = false; Grid.SetColumnSpan(List, 2);
                Grid.SetRow(OutCard, 3); Grid.SetColumn(OutCard, 0); Grid.SetColumnSpan(OutCard, 2); OutCard.HeightRequest = PaneLayout.StackedHeight(_h); break;
            default: // narrow and short: list or output, toggled from the strip
                OutToggle.IsVisible = true; OutToggle.Text = _showOut ? "Show scripts" : "Show output";
                List.IsVisible = !_showOut; OutCard.IsVisible = _showOut; Grid.SetColumnSpan(List, 2);
                Grid.SetRow(OutCard, 2); Grid.SetColumn(OutCard, 0); Grid.SetColumnSpan(OutCard, 2); OutCard.HeightRequest = -1; break;
        }
    }
    private void OnOutToggle(object? s, EventArgs e) { _showOut = !_showOut; ApplyLayout(); }

    public ScriptsPage()
    {
        InitializeComponent();
        string dir = Path.Combine(FileSystem.AppDataDirectory, "scripts");
        AppSettingsStore.FolderOverride = Path.Combine(FileSystem.AppDataDirectory, "settings");
        AppSettingsStore.Current.ScriptsFolderPath = dir;
        Reload();
    }

    private void Reload()
    {
        try
        {
            var items = _catalog.GetScripts();
            List.ItemsSource = items;
            Info.Text = items.Count == 0 ? "No scripts yet. Tap 'Update official scripts' (downloads the official set from the upstream project) or put .sh files in: " + _catalog.CustomScriptsFolderPath : items.Count + " scripts";
        }
        catch (Exception ex) { Info.Text = "List error: " + ex.Message; }
    }

    private void OnReload(object? s, EventArgs e) => Reload();

    private void Append(string line)
    {
        _out.AppendLine(line); if (_out.Length > 40000) _out.Remove(0, _out.Length - 30000);
        MainThread.BeginInvokeOnMainThread(async () => { Out.Text = _out.ToString(); await OutScroll.ScrollToAsync(0, 1e7, false); });
    }

    private async void OnUpdate(object? s, EventArgs e)
    {
        if (!await DisplayAlert("Update official scripts", "Internet only. This does NOT use the unit and does not need a unit connection. It downloads the official script set from the upstream project and replaces the local official copies (checked against its manifest). If this device is only on the unit's Wi-Fi it has no internet: update before connecting to the unit. Continue?", "Update", "Cancel")) return;
        try { Info.Text = "Updating..."; var msg = await Task.Run(() => _updater.UpdateOfficialScriptsAsync(_catalog.OfficialScriptsFolderPath)); Info.Text = "Update finished. Details are in the output pane."; _out.Clear(); _out.AppendLine(msg); Out.Text = _out.ToString(); Reload(); }
        catch (Exception ex)
        {
            string detail = NetDiag.Chain(ex), net = NetDiag.Snapshot();
            Breadcrumb.Mark("update failed: " + detail + " | " + net);
            Info.Text = "Update failed. It needs internet, not the unit. If this device is only on the unit's Wi-Fi, disconnect from it or turn on mobile data / another network and try again. Nothing was changed: the scripts already on this device (bundled or last updated) stay available. Cause: " + detail + ". " + net;
        }
    }

    private async void OnCopyLog(object? s, EventArgs ev0) => await UiSafe.Run(() => OnCopyLog_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnCopyLog_Impl(object? s, EventArgs e) { await Clipboard.Default.SetTextAsync(_out.ToString()); Info.Text = _out.Length == 0 ? "The log is empty." : "Log copied (" + _out.Length + " characters)."; }
    private void OnClearLog(object? s, EventArgs e) { _out.Clear(); Out.Text = ""; Info.Text = "Log cleared."; }

    private async void OnImport(object? s, EventArgs e)
    {
        try
        {
            var f = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Pick a .sh script" });
            if (f is null) return;
            if (!f.FileName.EndsWith(".sh", StringComparison.OrdinalIgnoreCase)) { Info.Text = "Only .sh scripts can be imported."; return; }
            _catalog.EnsureScriptsFolderExists(); Directory.CreateDirectory(_catalog.CustomScriptsFolderPath);
            string dest = Path.Combine(_catalog.CustomScriptsFolderPath, Path.GetFileName(f.FileName));
            if (File.Exists(dest) && !await DisplayAlert("Import", Path.GetFileName(dest) + " already exists in Custom. Replace it?", "Replace", "Cancel")) return;
            using (var src = await f.OpenReadAsync()) using (var dst = File.Create(dest)) await src.CopyToAsync(dst);
            Info.Text = "Imported into Custom: " + Path.GetFileName(dest); Reload();
        }
        catch (Exception ex) { Info.Text = "Import failed: " + ex.Message; }
    }

    // Official scripts are checked against the local manifest SHA-256 before anything runs (same rule as upstream).
    private async Task<bool> IntegrityOkAsync(ScriptItem sc)
    {
        if (!sc.IsOfficial) return true;
        string? expected = _updater.GetExpectedSha256(_catalog.OfficialScriptsFolderPath, sc);
        if (string.IsNullOrWhiteSpace(expected)) { Info.Text = "Official script integrity unavailable (no SHA in the local manifest). Run 'Update official scripts'."; return false; }
        string actual = sc.IsPackage ? ScriptIntegrityService.ComputePackageSha256(sc.PackageRootPath) : ScriptIntegrityService.ComputeSingleScriptSha256(sc.LocalPath);
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
        if (await DisplayAlert("Integrity check failed", sc.Name + " was modified locally or is out of sync with the official manifest. It will not run. Restore the official version now (downloads it)?", "Restore", "Cancel"))
        {
            try { Info.Text = "Restoring..."; Info.Text = await Task.Run(() => _updater.RestoreOfficialScriptAsync(_catalog.OfficialScriptsFolderPath, sc)); Reload(); }
            catch (Exception ex) { Info.Text = "Restore failed: " + ex.Message; }
        }
        return false;
    }

    private bool _running;
    private void OnCancel(object? s, EventArgs e) => _cts?.Cancel();

    private async void OnTapped(object? s, TappedEventArgs ev0) => await UiSafe.Run(() => OnTapped_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnTapped_Impl(object? s, TappedEventArgs e)
    {
        if ((s as BindableObject)?.BindingContext is not ScriptItem sc) return;
        var c = await this.Sheet(sc.Name, "Cancel", null, Session.UnitWritesLocked ? new[] { "Details", sc.IsOfficial ? "View source" : "Edit script" } : new[] { "Run on the unit", "Details", sc.IsOfficial ? "View source" : "Edit script" });
        if (c == "Details") { await DisplayAlert(sc.Name, $"{sc.Description}\n\nType: {sc.ScriptType}\nVersion: {sc.VersionDisplay}\nAuthor: {sc.AuthorDisplay}\nOfficial: {sc.IsOfficial}{(sc.IsModified ? " (modified from the official copy)" : "")}\nFile: {sc.LocalPath}", "OK"); return; }
        if (c == "Edit script" || c == "View source")
        {
            if (sc.IsPackage) { Info.Text = "Package scripts are edited as files inside " + sc.PackageRootPath; return; }
            await Navigation.PushAsync(new EditorPage(sc.LocalPath, true)); return;
        }
        if (c != "Run on the unit") return;
        if (Session.Svc is null || Session.Protocol != "SSH") { Info.Text = "Connect over SSH first."; return; }
        // A script header label (ReadOnly or not) is not proof of what the script does. Official scripts such as the Extract ones mount the SD card read-write and write files,
        // so every script, official or not, needs Write mode and a typed RUN.
        if (!Session.WriteMode) { Info.Text = "Running a script can change the unit or its SD card. Turn on Write mode in Settings first."; return; }
        if (_running) { Info.Text = "A script is already running. Cancel it first."; return; }
        Func<bool> allowed = () => Session.Protocol == "SSH" && Session.WriteMode;
        var res = await ConfirmedAction.RunAsync<IMibConnectionService>(() => Session.Svc, allowed,
            async () =>
            {
                string? typed = await DisplayPromptAsync("Run " + (sc.IsDangerousType ? "DANGEROUS" : "changing") + " script", sc.Name + " can change the unit " + Session.Host + (sc.IsModified ? " and differs from the official copy" : "") + ".\nType RUN to confirm.", "Run", "Cancel");
                return string.Equals(typed?.Trim(), "RUN", StringComparison.Ordinal);
            },
            async owner =>
            {
                if (!await IntegrityOkAsync(sc)) return;
                // the integrity step can show a dialog and download: check the owner and permission again before anything runs
                if (!ReferenceEquals(Session.Svc, owner) || !allowed()) { Info.Text = "The connection or Write mode changed. Nothing was run."; return; }
                _running = true; _showOut = true; ApplyLayout(); _out.Clear(); Out.Text = ""; CancelBtn.IsVisible = true; _cts = new CancellationTokenSource();
                try
                {
                    var exec = new ScriptExecutionService(owner);
                    var r = await Task.Run(() => exec.ExecuteAsync(sc, Append, _cts.Token));
                    Append(r.Success ? $"--- finished, exit {r.ExitCode}" : $"--- failed (exit {r.ExitCode}): {r.ErrorMessage}");
                }
                catch (OperationCanceledException oce) { Append(oce is ScriptCancelledException ? "--- " + oce.Message : "--- cancelled. The script may have run partly on the unit."); }
                catch (Exception ex) { Append("--- error: " + ex.Message + ". The script may have run partly on the unit."); }
                finally { CancelBtn.IsVisible = false; _running = false; }
            });
        if (res != GuardResult.Done && res != GuardResult.Declined) Info.Text = ConfirmedAction.Describe(res, "run");
    }
}
