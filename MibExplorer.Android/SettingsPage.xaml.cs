using MibExplorer.Services;
namespace MibExplorer.Android;

public partial class SettingsPage : ContentPage
{
    private bool _toKey;
    /// <summary>Opens Settings and, when asked, scrolls to the SSH key section once the page has been laid out.</summary>
    public void ShowKeySection() { _toKey = true; if (IsLoaded) _ = ScrollToKeyAsync(); }
    private async Task ScrollToKeyAsync()
    {
        if (!_toKey) return;
        await Task.Delay(250); // let the first layout pass finish so the section has a position
        try { _toKey = false; await Scroller.ScrollToAsync(SshCard, ScrollToPosition.Start, false); } catch { }
    }

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, __) => { if (_toKey) _ = ScrollToKeyAsync(); };
        Session.WriteModeChanged += () => MainThread.BeginInvokeOnMainThread(Refresh);
    }

    protected override void OnAppearing() { base.OnAppearing(); AppShell.PageShown("Settings"); Refresh(); }
    protected override void OnDisappearing() { base.OnDisappearing(); AppShell.PageHidden("Settings"); }

    private bool _busy;
    private void OnThemeChanged(object? s, EventArgs e)
    {
        if (_busy || ThemePicker.SelectedIndex < 0) return;
        string m = ThemePicker.SelectedIndex == 0 ? "system" : ThemePicker.SelectedIndex == 1 ? "day" : "night";
        if (m == Theme.Mode) return;
        Theme.SetMode(m);
    }

    private void Refresh()
    {
        _busy = true;
        ScriptsFolderLbl.Text = System.IO.Path.Combine(FileSystem.AppDataDirectory, "scripts");
        WriteSwitch.IsEnabled = Session.IsConnected; WriteSwitch.IsToggled = Session.WriteMode;
        WriteText.Text = "Write mode (this connection): " + (!Session.IsConnected ? "off, connect first" : Session.WriteMode ? "ON, changes are made on the unit" : "off, read-only");
        if (ThemePicker.ItemsSource is null) ThemePicker.ItemsSource = new List<string> { "Automatic (follow Device)", "Day", "Night" };
        ThemePicker.SelectedIndex = Theme.Mode == "system" ? 0 : Theme.Mode == "day" ? 1 : 2;
        bool writeOn = Session.IsConnected && Session.WriteMode;   // display only: reads the existing state, changes no gate
        WriteCard.Stroke = Theme.C(writeOn ? "WriteOnStroke" : "Accent");
        WriteHeaderBox.Background = Theme.B(writeOn ? "GlassHeaderDanger" : "GlassHeader");
        _busy = false;
        _ = RefreshKeyAsync();
        try { int n = new HostKeyVerifier(System.IO.Path.Combine(FileSystem.AppDataDirectory, "known_hosts.json")).List().Count; HostsInfo.Text = n == 0 ? "None pinned" : n + " pinned"; } catch { HostsInfo.Text = "Could not read the trusted hosts."; }
    }

    private async Task RefreshKeyAsync()
    {
        try { KeyInfo.Text = string.IsNullOrEmpty(await SecureStorage.Default.GetAsync("ssh_key_pem")) ? "None yet" : "Generated"; }
        catch { KeyInfo.Text = "Secure storage is not available."; }
    }

    private async Task<string?> PublicKeyAsync()
    {
        string? pub = await SecureStorage.Default.GetAsync("ssh_key_pub");
        if (!string.IsNullOrEmpty(pub)) return pub;
        string? pem = await SecureStorage.Default.GetAsync("ssh_key_pem"); if (string.IsNullOrEmpty(pem)) return null;
        var r = SshKeyService.InspectImported(pem, await SecureStorage.Default.GetAsync("ssh_key_pass") ?? "");
        return r.Valid ? r.PublicOpenSsh : null;
    }

    private async void OnShowPub(object? s, EventArgs e)
    {
        try { var pub = await PublicKeyAsync(); await DisplayAlert("Public key", pub ?? "No public key is available (no key yet, or a key type whose public part cannot be shown).", "OK"); }
        catch (Exception ex) { KeyInfo.Text = "Key error: " + ex.Message; }
    }

    private async void OnCopyPub(object? s, EventArgs e)
    {
        try { var pub = await PublicKeyAsync(); if (pub is null) { KeyInfo.Text = "No public key to copy."; return; } await Clipboard.Default.SetTextAsync(pub.Trim()); KeyInfo.Text = "Public key copied."; }
        catch (Exception ex) { KeyInfo.Text = "Key error: " + ex.Message; }
    }

    private async void OnNewKey(object? s, EventArgs ev0) => await UiSafe.Run(() => OnNewKey_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnNewKey_Impl(object? s, EventArgs e)
    {
        bool had = !string.IsNullOrEmpty(await SecureStorage.Default.GetAsync("ssh_key_pem"));
        if (!await DisplayAlert("Generate a new key?", (had ? "The current key is kept as 'previous' and the new key is used from the next connect. If the unit does not accept the new public key, you will not be able to connect over SSH with this key until you restore the previous one (SD Packages > previous key) or install the new public key." : "A new key is created and stored on this device.") + await KeyRotation.DiscardNoteAsync(), "Generate", "Cancel")) return;
        try
        {
            var (pem, pub) = SshKeyService.GenerateInMemory("mibexplorer-android");
            await KeySlots.RotateAsync(SecureKeyStore.Instance, pem, pub);
            KeyInfo.Text = "New key created. " + (had ? "The previous key is kept." : "") + " Use Show public key to read it."; 
        }
        catch (Exception ex) { KeyInfo.Text = "Key error: " + ex.Message; }
    }


    private async void OnImportKey(object? s, EventArgs ev0) => await UiSafe.Run(() => OnImportKey_Impl(), m => DisplayAlert("Error", m, "OK"));
    private async Task OnImportKey_Impl()
    {
        var pick = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a private key file (PEM / OpenSSH)" });
        if (pick is null) return;
        string pem; try { using (var st = await pick.OpenReadAsync()) { if (st.Length > 65536) { KeyInfo.Text = "That file is too large for a key."; return; } using var rd = new StreamReader(st); pem = await rd.ReadToEndAsync(); } }
        finally { try { if (!string.IsNullOrEmpty(pick.FullPath) && pick.FullPath.StartsWith(FileSystem.CacheDirectory) && File.Exists(pick.FullPath)) File.Delete(pick.FullPath); } catch { } }
        string pass = KeyPassEntry.Text ?? "";
        var r = SshKeyService.InspectImported(pem, pass);
        if (!r.Valid) { KeyInfo.Text = r.Message; return; }
        if (!await DisplayAlert("Replace the saved key?", "The saved SSH key on this device is replaced by the imported one. The old private key cannot be recovered from the app. Continue?", "Replace", "Cancel")) return;
        await SecureStorage.Default.SetAsync("ssh_key_pem", pem);
        if (pass.Length > 0) await SecureStorage.Default.SetAsync("ssh_key_pass", pass); else SecureStorage.Default.Remove("ssh_key_pass");
        if (r.PublicOpenSsh != null) await SecureStorage.Default.SetAsync("ssh_key_pub", r.PublicOpenSsh); else SecureStorage.Default.Remove("ssh_key_pub");
        KeyPassEntry.Text = "";
        KeyInfo.Text = r.Message + " Stored encrypted with the Android Keystore.";
    }

    private async void OnHosts(object? s, EventArgs e)
    {
        try
        {
            var v = new HostKeyVerifier(System.IO.Path.Combine(FileSystem.AppDataDirectory, "known_hosts.json"));
            var list = v.List(); if (list.Count == 0) { await DisplayAlert("Trusted hosts", "None yet. A host appears here after you trust its key on connect.", "OK"); return; }
            var pick = await this.Sheet("Trusted hosts (tap one to forget it)", "Close", null, list.Select(k => k.Key + "\n" + k.Value).ToArray());
            if (pick is null || pick == "Close") return;
            string host = pick.Split('\n')[0];
            if (await DisplayAlert("Forget " + host + "?", "The app will ask again the next time you connect to it.", "Forget", "Cancel")) { v.Forget(host); Refresh(); }
        }
        catch (Exception ex) { HostsInfo.Text = "Error: " + ex.Message; }
    }

    private async void OnWriteToggled(object? s, ToggledEventArgs ev0) => await UiSafe.Run(() => OnWriteToggled_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnWriteToggled_Impl(object? s, ToggledEventArgs e)
    {
        if (_busy) return;
        if (e.Value && Session.UnitWritesLocked) { await DisplayAlert("Write mode locked", "Changing anything on the unit is switched off in this build.", "OK"); Refresh(); return; }
        if (e.Value)
        {
            bool ok = await DisplayAlert("Turn on Write mode?", "This lets the app change files on the unit (" + Session.Host + "). Mistakes on a car unit can be hard to undo.", "Turn on", "Cancel");
            if (!ok) { Refresh(); return; }
        }
        Session.WriteMode = e.Value; Refresh();
    }

    private async void OnLicenses(object? s, EventArgs ev0) => await UiSafe.Run(() => OnLicenses_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnLicenses_Impl(object? s, EventArgs e) => await Navigation.PushAsync(new LicensesPage());
    private async void OnAbout(object? s, EventArgs ev0) => await UiSafe.Run(() => Navigation.PushAsync(new AboutPage()), m => DisplayAlert("Error", m, "OK"));

    private async void OnDiag(object? s, EventArgs ev0) => await UiSafe.Run(() => OnDiag_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnDiag_Impl(object? s, EventArgs e)
    {
        string Read(string n) { try { var f = Path.Combine(FileSystem.AppDataDirectory, "..", n); f = File.Exists(f) ? f : Path.Combine("/data/data/com.instinct.mibexplorer/files", n); return File.Exists(f) ? string.Join("\n", File.ReadLines(f).TakeLast(30)) : "(none)"; } catch (Exception ex) { return "err " + ex.Message; } }
        var text = "startup-stage:\n" + Read("startup-stage.txt") + "\n\nlast-crash:\n" + Read("last-crash.txt");
        if (await DisplayAlert("Diagnostics (this app only)", text, "Share", "Close"))
            await Share.Default.RequestAsync(new ShareTextRequest { Text = text, Title = "MibExplorer diagnostics" });
    }
}
