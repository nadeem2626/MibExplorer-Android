using MibExplorer.Models;
using MibExplorer.Services;

namespace MibExplorer.Android;

public partial class ConnectPage : ContentPage
{
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0) return;
        // Wide: two columns. Narrow (any height): one scrolling column, so neither half is squeezed into a small region.
        bool wide = width >= 800;
        if (wide && RightStack.Parent is not ScrollView) { LeftStack.Children.Remove(RightStack); RightCol.Content = RightStack; }
        if (!wide && RightStack.Parent is ScrollView) { RightCol.Content = null; LeftStack.Children.Add(RightStack); }
        RightCol.IsVisible = wide; Grid.SetColumnSpan(LeftCol, wide ? 1 : 2);
    }

    public ConnectPage()
    {
        InitializeComponent();
        Session.ReconnectRequested += () => MainThread.InvokeOnMainThreadAsync(() => DoConnect());
        Session.StatusChanged += () => MainThread.BeginInvokeOnMainThread(() => { StatusLabel.Text = Session.Status; UpdateBanner(); });
        ProtocolPicker.ItemsSource = Profiles.All; ProtocolPicker.SelectedIndex = 0;
        ProtocolPicker.SelectedIndexChanged += OnProtocolChanged;
        PortEntry.Text = "22"; UserEntry.Text = "root";
        Reload();
    }

    private int _lastProtoIdx = 0;
    private void OnProtocolChanged(object? s, EventArgs e)
    {
        // Only follow the protocol with the default port/user when the user left the previous defaults alone.
        var prev = Profiles.All[Math.Max(0, _lastProtoIdx)]; var now = ProtocolPicker.SelectedItem as string ?? Profiles.All[0];
        if (PortEntry.Text == Profiles.DefaultPort(prev).ToString()) PortEntry.Text = Profiles.DefaultPort(now).ToString();
        if (UserEntry.Text == Profiles.DefaultUser(prev)) UserEntry.Text = Profiles.DefaultUser(now);
        _lastProtoIdx = ProtocolPicker.SelectedIndex;
        KeyHint.IsVisible = now.StartsWith("SSH");
    }

    private List<Profile> _profiles = new();
    private Profile? _current;

    private void Reload()
    {
        _profiles = Profiles.Load();
        UnitsList.Children.Clear();
        if (_profiles.Count == 0) UnitsList.Children.Add(new Label { Text = "No saved units yet.", TextColor = Theme.C("Muted") });
        for (int i = 0; i < _profiles.Count; i++)
        {
            var p = _profiles[i]; int idx = i; bool sel = _current != null && _current.Id == p.Id;
            string auth = p.RememberPassword ? "password saved" : p.Protocol == "SSH" ? "key login" : "password";
            var card = new Border { Padding = 12, Background = Theme.B(sel ? "GlassSelected" : "GlassCard"), Stroke = Theme.C(sel ? "SelectedStroke" : "GlassStroke"), StrokeThickness = sel ? 2 : 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 } };
            var col = new VerticalStackLayout { Spacing = 2 };
            col.Children.Add(new Label { Text = p.Name, FontAttributes = FontAttributes.Bold, FontSize = 16, TextColor = Theme.C("Text") });
            col.Children.Add(new Label { Text = $"{p.Host} \u00B7 {p.Protocol}:{p.Port} \u00B7 {p.Username} \u00B7 {auth}", TextColor = Theme.C("Muted"), FontSize = 13 });
            card.Content = col; var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => SelectProfile(idx); card.GestureRecognizers.Add(tap);
            UnitsList.Children.Add(card);
        }
    }

    private async void SelectProfile(int index)
    {
        if (index < 0 || index >= _profiles.Count) return;
        var p = _current = _profiles[index];
        NameEntry.Text = p.Name; HostEntry.Text = p.Host; PortEntry.Text = p.Port.ToString(); UserEntry.Text = p.Username;
        ProtocolPicker.SelectedItem = Profiles.All.Contains(p.Protocol) ? p.Protocol : Profiles.All[0];
        RememberCheck.IsChecked = p.RememberPassword;
        EditHeading.Text = "Unit details: " + p.Name; EditCard.IsVisible = true; Reload();
        int seq = ++_loadSeq;
        PassEntry.Text = "";
        if (!p.RememberPassword) { ConnectBtn.IsEnabled = true; return; }
        ConnectBtn.IsEnabled = false; // Connect waits until the remembered password has been loaded, so it cannot send an empty field
        try
        {
            var pw = await Profiles.GetPasswordAsync(p) ?? "";
            // A stale load must not overwrite another profile's form or text the user already typed.
            if (seq == _loadSeq && ReferenceEquals(_current, p) && string.IsNullOrEmpty(PassEntry.Text)) PassEntry.Text = pw;
        }
        catch (Exception ex) { if (seq == _loadSeq) Session.SetStatus("The remembered password could not be read: " + ex.Message); }
        finally { if (seq == _loadSeq) ConnectBtn.IsEnabled = true; }
    }
    private int _loadSeq;

    private void OnAddUnit(object? s, EventArgs e)
    {
        _loadSeq++; ConnectBtn.IsEnabled = true;
        _current = null; NameEntry.Text = ""; HostEntry.Text = ""; PassEntry.Text = ""; RememberCheck.IsChecked = false;
        ProtocolPicker.SelectedIndex = 0; PortEntry.Text = Profiles.DefaultPort(Profiles.All[0]).ToString(); UserEntry.Text = Profiles.DefaultUser(Profiles.All[0]);
        EditHeading.Text = "New unit"; EditCard.IsVisible = true; Reload();
    }

    private async void OnSaveProfile(object? s, EventArgs ev0) => await UiSafe.Run(() => OnSaveProfile_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnSaveProfile_Impl(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameEntry.Text) || string.IsNullOrWhiteSpace(HostEntry.Text)) { Session.SetStatus("Enter a profile name and host first"); return; }
        var p = _current is not null && _current.Name == NameEntry.Text.Trim() ? _current : new Profile();
        p.Name = NameEntry.Text.Trim(); p.Host = HostEntry.Text.Trim();
        p.Port = int.TryParse(PortEntry.Text, out var pt) ? pt : 22; p.Username = UserEntry.Text?.Trim() ?? "root";
        p.Protocol = ProtocolPicker.SelectedItem as string ?? Profiles.All[0]; p.RememberPassword = RememberCheck.IsChecked;
        if (!_profiles.Any(x => x.Id == p.Id)) _profiles.Add(p);
        Profiles.Save(_profiles);
        if (p.RememberPassword && !string.IsNullOrEmpty(PassEntry.Text)) await Profiles.SetPasswordAsync(p, PassEntry.Text);
        else if (!p.RememberPassword) Profiles.ClearPassword(p);
        _current = p; Reload(); Session.SetStatus("Profile saved: " + p.Name + (p.RememberPassword && string.IsNullOrEmpty(PassEntry.Text) ? ". No password typed, so any password stored earlier is kept and none was added." : ""));
    }

    private void OnDeleteProfile(object? s, EventArgs e)
    {
        if (_current is null) { Session.SetStatus("Pick a profile to delete"); return; }
        Profiles.ClearPassword(_current); _profiles.RemoveAll(x => x.Id == _current.Id); Profiles.Save(_profiles);
        Session.SetStatus("Profile deleted: " + _current.Name); _current = null; Reload();
    }

    private async void OnConnect(object? s, EventArgs ev0) => await UiSafe.Run(() => OnConnect_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnConnect_Impl(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(HostEntry.Text)) { EditCard.IsVisible = true; Session.SetStatus("Choose a saved unit or add one (IP, port, user) first."); return; }
        await DoConnect();
    }
    private readonly ConnectGate _gate = new();
    private async Task DoConnect()
    {
        var proto = ProtocolPicker.SelectedItem as string ?? Profiles.All[0];
        int port = 0;
        // Order is tested (ConnectGate): validate, refuse re-entry, only then drop the working connection.
        string? err;
        try
        {
            err = await _gate.RunAsync(() => ConnectGate.ValidateInput(PortEntry.Text, HostEntry.Text, UserEntry.Text, proto.StartsWith("SSH"), out port),
                () => { Session.Svc?.Dispose(); Session.Svc = null; Session.Path = null; Session.Protocol = ""; Session.WriteMode = false; },
                () => ConnectCoreAsync(proto, port));
        }
        catch (Exception ex) { Session.SetStatus("Error: " + ex.Message); return; }
        if (err == "busy") Session.SetStatus("Already connecting. Wait for it to finish.");
        else if (err != null) Session.SetStatus(err);
    }

    // Remember password: stored only after the connection worked (a wrong password is never kept), in the Keystore-backed SecureStorage, never in the profile JSON.
    private async Task<string> RememberAfterConnectAsync(string proto, int port, string host, string user, string name, string pw, Profile? cur)
    {
        var list = Profiles.Load();
        var p = list.FirstOrDefault(x => cur != null && x.Id == cur.Id && x.Host == host && x.Port == port && x.Username == user && x.Protocol == proto)
             ?? list.FirstOrDefault(x => x.Host == host && x.Port == port && x.Username == user && x.Protocol == proto);
        bool created = p is null;
        if (p is null) { p = new Profile { Name = name.Length > 0 && !list.Any(x => x.Name == name) ? name : host, Host = host, Port = port, Username = user, Protocol = proto }; list.Add(p); }
        p.RememberPassword = true;
        await Profiles.SetPasswordAsync(p, pw);
        if (await Profiles.GetPasswordAsync(p) != pw) { p.RememberPassword = false; Profiles.Save(list); return "the password could NOT be stored (read-back failed)"; }
        Profiles.Save(list);
        await MainThread.InvokeOnMainThreadAsync(() => { _current = p; Reload(); });
        return (created ? "Saved unit and password: " : "Password saved for: ") + p.Name;
    }

    private async Task ConnectCoreAsync(string proto, int port)
    {
        IMibConnectionService svc;
            if (proto.StartsWith("SSH"))
            {
                var verifier = new HostKeyVerifier(Path.Combine(FileSystem.AppDataDirectory, "known_hosts.json"))
                {
                    // Runs on the worker thread; blocks it until the user answers.
                    ConfirmNewKey = (host, alg, fp) => MainThread.InvokeOnMainThreadAsync(() =>
                        DisplayAlert("New SSH host key", $"{host}\n{alg}\n{fp}\n\nTrust and remember this key? A later change will be refused.", "Trust", "Cancel")).GetAwaiter().GetResult()
                };
                svc = new SshMibConnectionService { HostKeys = verifier, RequireHostKeyPinning = true, KeepAliveSeconds = 15 };
            }
            else if (proto == "Telnet") svc = new TelnetMibConnectionService();
            else svc = new FtpMibConnectionService();
            svc.ConnectionStateChanged += (_, up) => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!up && Session.Svc == svc) { Session.Svc = null; }
                if (Session.Svc == svc || !up) Session.SetStatus(up ? "Connected" : "Disconnected (link lost or closed)");
            });
            bool ssh = proto.StartsWith("SSH");
            // Captured now: the remember choice is applied only after a successful connect, with exactly what was typed.
            bool remember = RememberCheck.IsChecked; string typedPw = PassEntry.Text ?? ""; string pName = NameEntry.Text?.Trim() ?? "";
            string pHost = HostEntry.Text.Trim(), pUser = UserEntry.Text.Trim(); var pCur = _current;
            var cs = new ConnectionSettings
            {
                Host = HostEntry.Text.Trim(), Port = port, Username = UserEntry.Text.Trim(),
                Password = PassEntry.Text ?? "", UsePrivateKey = ssh && string.IsNullOrEmpty(PassEntry.Text),
                PrivateKeyPem = ssh && string.IsNullOrEmpty(PassEntry.Text) ? (await SecureStorage.Default.GetAsync("ssh_key_pem") ?? "") : "",
                Passphrase = ssh && string.IsNullOrEmpty(PassEntry.Text) ? (await SecureStorage.Default.GetAsync("ssh_key_pass") ?? "") : ""
            };
            Session.SetStatus("Connecting (" + proto + ")...");
            try { await Task.Run(() => svc.ConnectAsync(cs)); }
            catch { try { svc.Dispose(); } catch { } throw; } // a failed attempt must not leave a half-open service behind
            Session.Svc = svc; Session.Host = cs.Host; Session.Port = port; Session.Protocol = ssh ? "SSH" : proto; Session.WriteMode = false;
            string? rememberNote = null;
            if (remember && typedPw.Length > 0)
            {
                try { rememberNote = await RememberAfterConnectAsync(proto, port, pHost, pUser, pName, typedPw, pCur); }
                catch (Exception ex) { rememberNote = "the password could NOT be stored: " + ex.Message; }
            }
            else if (remember) rememberNote = "Remember is on but no password was typed, so nothing was stored";
            Session.SetStatus("Connected: " + cs.Host + " (" + proto + ")" + (rememberNote is null ? "" : ". " + rememberNote));
            await Shell.Current.GoToAsync("//Files");
    }

    private async void OnDetect(object? s, EventArgs e)
    {
        try
        {
            Session.SetStatus("Detecting...");
            var r = await Task.Run(() => MibNetworkHelper.TryDetectMibGatewayAsync());
            if (r is null || string.IsNullOrWhiteSpace(r.GatewayIp)) { Session.SetStatus("No Wi-Fi gateway found. Join the unit's Wi-Fi, tap Use Wi-Fi network, or type the IP."); return; }
            int port = int.TryParse(PortEntry.Text, out var pp) ? pp : 22;
            bool open = await RebootWatcher.DefaultProbe(r.GatewayIp, port, default);
            if (open) { HostEntry.Text = r.GatewayIp; Session.SetStatus($"Found {r.GatewayIp} answering on port {port}. It was filled in. Check it is your unit before connecting."); }
            else Session.SetStatus($"Wi-Fi gateway is {r.GatewayIp} but port {port} does not answer, so the IP was not filled in.");
        }
        catch (Exception ex) { Session.SetStatus("Detect failed: " + ex.Message); }
    }

    private void OnBind(object? s, EventArgs e) => Session.SetStatus(WifiBinder.Bind());

    private async void OnOpenKeySettings(object? s, EventArgs ev0) => await UiSafe.Run(async () =>
    {
        // Repeated taps must not stack pages: reuse Settings when it is already on top.
        if (Navigation.NavigationStack.LastOrDefault() is SettingsPage open) { open.ShowKeySection(); return; }
        var page = new SettingsPage(); page.ShowKeySection(); await Navigation.PushAsync(page);
    }, m => DisplayAlert("Error", m, "OK"));

    // Clear state banner: text AND colour (green Connected, grey Disconnected).
    private void UpdateBanner()
    {
        bool up = Session.IsConnected;
        StateLabel.Text = up ? "\u25CF  Connected: " + Session.Host + " (" + Session.Protocol + ")" : "\u25CB  Disconnected";
        StateLabel.TextColor = up ? Colors.White : Theme.C("OnOff");
        StateBanner.Background = Theme.B(up ? "GlassConnected" : "GlassOff");
    }
    protected override void OnAppearing() { base.OnAppearing(); UpdateBanner(); }
}
