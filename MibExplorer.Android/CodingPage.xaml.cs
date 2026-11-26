using MibExplorer.Services;
using System.Text;
using MibExplorer.Models.Coding;
using MibExplorer.Services.Coding;

namespace MibExplorer.Android;

public partial class CodingPage : ContentPage
{
    private byte[] _orig = Array.Empty<byte>();
    private byte[] _cur = Array.Empty<byte>();
    private string _vin = "";
    private string _tab = "F";
    private readonly CodingFeatureService _features = new();
    private readonly CodingHistoryService _history = new();
    private bool _catalog;
    private List<CodingHistorySnapshot> _snaps = new();

    public CodingPage()
    {
        InitializeComponent();
        CodingHistoryService.RootOverride = FileSystem.AppDataDirectory;
        Session.WriteModeChanged += () => MainThread.BeginInvokeOnMainThread(Refresh);
        Refresh();
    }

    private void OnTab(object? s, EventArgs e) { _tab = (string)((Button)s!).CommandParameter; Refresh(); }
    private void OnSearch(object? s, TextChangedEventArgs e) { if (_tab == "F") Refresh(); }
    // Fail closed: the feature list is a generic MIB2 5F catalog with inferred entries, there is no evidence it applies to the connected unit,
    // and a write replaces the whole 25 byte blob. Reading, decoding, raw view, snapshots and history stay available. Turn on only with applicability evidence.
    private const bool WritesAvailable = true;
    // You can change values on this screen (working copy only, nothing leaves the tablet). Sending them to the unit stays locked.
    private const string ReadOnlyNote = "Writing to the unit is locked: the feature list is a generic MIB2 5F catalog that has not been verified for this unit. Your changes stay on this screen.";
    private bool Loaded => _orig.Length > 0;


    private void OnResultOk(object? s, EventArgs e) => ResultCard.IsVisible = false;

    // kind: 0 = written OK (read-back equals target), 1 = not written, 2 = not confirmed / failed
    private void ShowResult(int kind, string head, string why, string next)
    {
        Color c = kind == 0 ? Color.FromArgb("#1B7F3B") : kind == 1 ? Color.FromArgb("#B26A00") : Color.FromArgb("#B3261E");
        ResultHead.Text = head; ResultHead.TextColor = c;
        ResultWhy.Text = why; ResultNext.Text = next;
        ResultCard.Stroke = c; ResultCard.StrokeThickness = 3; ResultCard.IsVisible = true;
    }

    private async void OnRead(object? s, EventArgs e)
    {
        if (Session.UnitWritesLocked) { Status.Text = Session.LockText; return; }
        if (Session.Svc is null) { Status.Text = "Not connected"; return; }
        try
        {
            Status.Text = "Reading...";
            var readSvc = Session.Svc; var svc = new CodingCenterService(readSvc);
            var r = await Task.Run(() => svc.Read5FCodingAsync(null));
            if ((r.CodingHex ?? "").Length != 50) { _orig = Array.Empty<byte>(); _cur = _orig; Vin.Text = $"VIN: {r.Vin}"; Status.Text = $"Unexpected coding size ({(r.CodingHex ?? "").Length / 2} bytes, expected 25). This unit does not match the MIB2 5F layout this catalog describes, so nothing is shown or editable."; Refresh(); return; }
            _orig = CodingEditing.FromHex(r.CodingHex); _cur = (byte[])_orig.Clone(); _vin = r.Vin; _boundSvc = readSvc;
            if (!_catalog) { await _features.LoadAsync(""); _catalog = true; }
            Vin.Text = $"VIN: {r.Vin}   Bytes: {r.ByteCount}";
            _snaps = await _history.LoadSnapshotsAsync(_vin);
            Status.Text = "Read OK";
        }
        catch (Exception ex) { Status.Text = "Read failed: " + ex.Message; }
        Refresh();
    }

    private List<CodingPendingChange> Pending => Loaded ? CodingEditing.Changes(_features, _orig, _cur) : new();
    private int ByteDiffs => Enumerable.Range(0, _cur.Length).Count(i => _cur[i] != _orig[i]);

    private void Refresh()
    {
        Search.IsVisible = _tab == "F";
        foreach (var (b, k) in new[] { (TabF, "F"), (TabR, "R"), (TabC, "C"), (TabH, "H") })
        { b.Background = k == _tab ? UiGlass.Primary : UiGlass.Secondary; b.TextColor = k == _tab ? Theme.C("OnPrimary") : Theme.C("OnSecondary"); }
        Body.Children.Clear(); Detail.Children.Clear(); ApplyLayout();
        if (!Loaded) { Body.Children.Add(new Label { Text = "Read the coding first.", TextColor = Theme.C("Muted") }); WriteBtn.IsEnabled = false; return; }
        switch (_tab) { case "F": BuildFeatures(); break; case "R": BuildRaw(); break; case "C": BuildChanges(); break; case "H": BuildHistory(); break; }
        WriteBtn.IsEnabled = WritesAvailable && Session.WriteMode && ByteDiffs > 0;
        WriteBtn.Text = ByteDiffs > 0 ? $"Write coding ({ByteDiffs} byte(s) changed)" : "Write coding";
        if (!Session.WriteMode) Status.Text = "Write mode is off (Settings).";
    }

    private string? _selId;
    protected override void OnSizeAllocated(double width, double height) { base.OnSizeAllocated(width, height); if (width > 0) _w = width; if (height > 0) _h = height; ApplyLayout(); }
    private double _w, _h; private bool _drill;
    private void ApplyLayout()
    {
        bool split = _tab == "F" && Loaded;
        if (!split) { DetailCard.IsVisible = false; ListScroll.IsVisible = true; Grid.SetColumnSpan(ListScroll, 2); return; }
        switch (PaneLayout.Choose(_w, _h))
        {
            case PaneMode.Side:
                ListScroll.IsVisible = true; DetailCard.IsVisible = true; Grid.SetColumnSpan(ListScroll, 1);
                Grid.SetRow(DetailCard, 2); Grid.SetColumn(DetailCard, 1); Grid.SetColumnSpan(DetailCard, 1); DetailCard.HeightRequest = -1; break;
            case PaneMode.Stacked:
                ListScroll.IsVisible = true; DetailCard.IsVisible = true; Grid.SetColumnSpan(ListScroll, 2);
                Grid.SetRow(DetailCard, 3); Grid.SetColumn(DetailCard, 0); Grid.SetColumnSpan(DetailCard, 2); DetailCard.HeightRequest = PaneLayout.StackedHeight(_h); break;
            default: // Drill: list full area; detail replaces it after a tap
                ListScroll.IsVisible = !_drill; DetailCard.IsVisible = _drill; Grid.SetColumnSpan(ListScroll, 2);
                Grid.SetRow(DetailCard, 2); Grid.SetColumn(DetailCard, 0); Grid.SetColumnSpan(DetailCard, 2); DetailCard.HeightRequest = -1; break;
        }
    }

    private void BuildFeatures()
    {
        string q = (Search.Text ?? "").Trim();
        var list = _features.Decode(CodingEditing.ToBytes(_cur));
        var orig = _features.Decode(CodingEditing.ToBytes(_orig)).ToDictionary(f => f.Id);
        if (_selId is null || !list.Any(f => f.Id == _selId)) _selId = list.FirstOrDefault()?.Id;
        foreach (var f in list)
        {
            if (q.Length > 0 && !(f.Label + " " + f.Key + " " + f.Id).Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            bool changed = orig.TryGetValue(f.Id, out var o) && o.RawValue != f.RawValue;
            bool sel = f.Id == _selId;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(12, 4), MinimumHeightRequest = 56, BackgroundColor = sel ? Theme.C("SelTint2") : Colors.Transparent };
            var l = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center };
            l.Children.Add(new Label { Text = f.Label, FontSize = 15, LineBreakMode = LineBreakMode.TailTruncation });
            l.Children.Add(new Label { Text = $"byte {f.Byte}  mask 0x{f.Mask:X2}", FontSize = 11, TextColor = Theme.C("Muted") });
            row.Add(l, 0);
            row.Add(new Label { Text = f.Value + (changed ? " *" : ""), FontSize = 13, TextColor = changed ? Theme.C("Warn") : Theme.C("Muted"), VerticalOptions = LayoutOptions.Center, Margin = new Thickness(8, 0, 0, 0) }, 1);
            var id = f.Id;
            row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { _selId = id; _drill = true; Refresh(); }) });
            Body.Children.Add(row);
        }
        if (Body.Children.Count == 0) Body.Children.Add(new Label { Text = "No feature matches.", TextColor = Theme.C("Muted"), Margin = new Thickness(12) });
        BuildDetail(list.FirstOrDefault(f => f.Id == _selId));
    }

    // Right-hand pane: the selected feature and its catalog options. Tapping a value changes the on-screen working copy only; sending to the unit needs Write mode, SSH and the typed CODING confirmation.
    private void BuildDetail(CodingFeatureValue? f)
    {
        Detail.Children.Clear();
        if (f is null) return;
        if (PaneLayout.Choose(_w, _h) == PaneMode.Drill)
        {
            var back = new Button { Text = "\u2039 Back to list", HeightRequest = 48, MinimumHeightRequest = 48, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(8, 8, 0, 0), Background = UiGlass.Secondary, TextColor = Theme.C("OnSecondary") };
            back.Clicked += (s, e) => { _drill = false; Refresh(); };
            Detail.Children.Add(back);
        }
        var head = new VerticalStackLayout { Padding = new Thickness(12, 10, 12, 6) };
        head.Children.Add(new Label { Text = f.Label, FontSize = 18, FontAttributes = FontAttributes.Bold });
        head.Children.Add(new Label { Text = $"byte {f.Byte}  mask 0x{f.Mask:X2}  shift {f.Shift}  read value: {f.Value} ({f.RawValue})", FontSize = 11, TextColor = Theme.C("Muted") });
        Detail.Children.Add(head);
        Detail.Children.Add(new Label { Text = "Tap a value to change it on this screen. Nothing is sent to the unit: writing is locked until this catalog is verified for it.", FontSize = 12, TextColor = Theme.C("Warn"), Margin = new Thickness(12, 0, 12, 6) });
        foreach (var opt in f.Options)
        {
            bool cur = opt.Raw == f.RawValue;
            var r = new HorizontalStackLayout { Spacing = 12, Padding = new Thickness(12, 0), HeightRequest = 48 };
            r.Children.Add(new Label { Text = cur ? "\u25C9" : "\u25CB", FontSize = 20, VerticalOptions = LayoutOptions.Center, TextColor = cur ? Theme.C("Accent") : Theme.C("Dim2") });
            r.Children.Add(new Label { Text = $"{opt.Label}  ({opt.Raw})", FontSize = 15, VerticalOptions = LayoutOptions.Center, TextColor = cur ? Theme.C("Text") : Theme.C("Muted") });
            var feat = f; var o = opt;
            r.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() =>
            {
                if (o.Raw == feat.RawValue) return;
                if (!int.TryParse(o.Raw, out int raw) || !CodingEditing.SetField(_cur, feat.Byte, feat.Mask, feat.Shift, raw)) { Status.Text = "Value does not fit this field."; return; }
                Refresh();
            }) });
            Detail.Children.Add(r);
        }
    }

    private void BuildRaw()
    {
        Body.Children.Add(new Label { Text = "Tap a bit to flip it. Bit 7 is on the left.", FontSize = 11, TextColor = Theme.C("Muted") });
        for (int i = 0; i < _cur.Length; i++)
        {
            int bi = i;
            var row = new HorizontalStackLayout { Spacing = 3 };
            row.Children.Add(new Label { Text = $"{i,3}", FontFamily = "monospace", WidthRequest = 30, VerticalOptions = LayoutOptions.Center, TextColor = _cur[i] != _orig[i] ? Theme.C("Warn") : Theme.C("Muted") });
            row.Children.Add(new Label { Text = _cur[i].ToString("X2"), FontFamily = "monospace", FontAttributes = FontAttributes.Bold, WidthRequest = 28, VerticalOptions = LayoutOptions.Center });
            for (int bit = 7; bit >= 0; bit--)
            {
                int bb = bit; bool on = (_cur[i] & (1 << bit)) != 0; bool diff = on != ((_orig[i] & (1 << bit)) != 0);
                var b = new Button { Text = on ? "1" : "0", WidthRequest = 48, HeightRequest = 48, MinimumHeightRequest = 48, Padding = 0, FontSize = 12, CornerRadius = 6,
                    Background = diff ? UiGlass.Amber : on ? UiGlass.Primary : UiGlass.Secondary, TextColor = on || diff ? Theme.C("OnPrimary") : Theme.C("OnSecondary") };
                b.Clicked += (s, e) => { CodingEditing.SetBit(_cur, bi, bb, (_cur[bi] & (1 << bb)) == 0); Refresh(); };
                row.Children.Add(b);
            }
            Body.Children.Add(row);
        }
        var hex = new Editor { Text = CodingEditing.ToHex(_cur), FontFamily = "monospace", TextColor = Theme.C("Text"), AutoSize = EditorAutoSizeOption.TextChanges };
        var apply = new Button { Text = "Apply hex" };
        apply.Clicked += (s, e) =>
        {
            try { var n = CodingEditing.FromHex(hex.Text ?? ""); if (n.Length != _orig.Length) throw new FormatException($"Length must stay {_orig.Length} bytes."); _cur = n; Refresh(); }
            catch (Exception ex) { Status.Text = "Hex rejected: " + ex.Message; }
        };
        var reset = new Button { Text = "Reset to what was read", Background = UiGlass.Secondary, TextColor = Theme.C("OnSecondary") };
        reset.Clicked += (s, e) => { _cur = (byte[])_orig.Clone(); Refresh(); };
        Body.Children.Add(hex); Body.Children.Add(apply); Body.Children.Add(reset);
    }

    private void BuildChanges()
    {
        var sb = new StringBuilder();
        var p = Pending;
        sb.AppendLine($"Features changed: {p.Count}");
        foreach (var c in p) sb.AppendLine($"- {c.Label}: {c.CurrentValue} ({c.CurrentRawValue}) > {c.NewValue} ({c.NewRawValue})  [byte {c.Byte}]");
        sb.AppendLine().AppendLine($"Bytes changed: {ByteDiffs}");
        for (int i = 0; i < _cur.Length; i++) if (_cur[i] != _orig[i]) sb.AppendLine($"[{i}] {_orig[i]:X2} > {_cur[i]:X2}");
        if (p.Count == 0 && ByteDiffs == 0) sb.Clear().Append("No change.");
        Body.Children.Add(new Border { Content = new Label { Text = sb.ToString(), FontFamily = "monospace", FontSize = 12 } });
    }

    private void BuildHistory()
    {
        var save = new Button { Text = "Save current values as snapshot" };
        save.Clicked += async (s, e) => await UiSafe.Run(async () =>
        {
            string? c = await DisplayPromptAsync("Snapshot", "Comment (optional)", "Save", "Cancel");
            if (c is null) return;
            await _history.AddSnapshotAsync(_vin, CodingEditing.ToHex(_cur), _cur.Length, "Manual", Pending, c);
            _snaps = await _history.LoadSnapshotsAsync(_vin); Refresh();
        }, m => DisplayAlert("Error", m, "OK"));
        Body.Children.Add(save);
        if (_snaps.Count == 0) Body.Children.Add(new Label { Text = "No snapshots for this VIN yet. One is saved automatically before every write.", FontSize = 12, TextColor = Theme.C("Muted") });
        foreach (var sn in _snaps.OrderByDescending(x => x.CreatedAt))
        {
            var snap = sn;
            var st = new VerticalStackLayout { Spacing = 4 };
            st.Children.Add(new Label { Text = $"{snap.CreatedAt:yyyy-MM-dd HH:mm}  {snap.Reason}  ({snap.ByteCount} bytes)", FontAttributes = FontAttributes.Bold });
            if (!string.IsNullOrWhiteSpace(snap.Comment)) st.Children.Add(new Label { Text = snap.Comment, FontSize = 12 });
            var load = new Button { Text = "Load into editor", Background = UiGlass.Secondary, TextColor = Theme.C("OnSecondary") };
            load.Clicked += (s, e) =>
            {
                byte[] b; try { b = CodingEditing.FromHex(snap.CodingHex); } catch (Exception ex) { Status.Text = "This snapshot is damaged: " + ex.Message; return; }
                if (b.Length != _orig.Length) { Status.Text = "Snapshot length differs from the unit's coding."; return; }
                _cur = b; _tab = "C"; Refresh();
            };
            var del = new Button { Text = "Delete", Background = UiGlass.Card, TextColor = Theme.C("DangerText") };
            del.Clicked += async (s, e) => await UiSafe.Run(async () => { await _history.DeleteSnapshotAsync(_vin, snap); _snaps = await _history.LoadSnapshotsAsync(_vin); Refresh(); }, m => DisplayAlert("Error", m, "OK"));
            st.Children.Add(new HorizontalStackLayout { Spacing = 8, Children = { load, del } });
            Body.Children.Add(new Border { Content = st });
        }
    }

    private object? _boundSvc;
    private async void OnWrite(object? s, EventArgs ev0) => await UiSafe.Run(() => OnWrite_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnWrite_Impl(object? s, EventArgs e)
    {
        if (!WritesAvailable) { Status.Text = ReadOnlyNote; return; }
        if (Session.Svc is null || !Session.WriteMode || ByteDiffs == 0) return;
        var wsvc = Session.Svc; // this operation belongs to this connection only
        if (!ReferenceEquals(_boundSvc, Session.Svc)) { ShowResult(1, "NOT WRITTEN", "The connection changed since the coding was read.", "Tap Read from unit, then try again."); return; }
        if (Session.Protocol != "SSH") { ShowResult(1, "NOT WRITTEN", "Coding writes need an SSH connection.", "Connect with SSH, then read the coding again."); return; }
        string target = CodingEditing.ToHex(_cur);
        var p = Pending;
        var sb = new StringBuilder();
        foreach (var c in p.Take(12)) sb.AppendLine($"{c.Label}: {c.CurrentValue} > {c.NewValue}");
        if (p.Count > 12) sb.AppendLine($"... and {p.Count - 12} more");
        sb.AppendLine($"{ByteDiffs} byte(s) differ. The current coding is saved to History first.");
        var res = await ConfirmedAction.RunAsync<MibExplorer.Services.IMibConnectionService>(
            () => ReferenceEquals(Session.Svc, wsvc) && ReferenceEquals(_boundSvc, wsvc) ? wsvc : null,
            () => Session.WriteMode && Session.Protocol == "SSH",
            async () => string.Equals((await DisplayPromptAsync("Write coding", sb + "\nThis writes the full coding blob to " + Session.Host + ". The feature list is a generic MIB2 5F catalog and was not checked against this unit's model, so the meaning of each bit on this unit is not verified. Type CODING to confirm.", "Write", "Cancel"))?.Trim(), "CODING", StringComparison.Ordinal),
            async owner =>
            {
                Status.Text = "Re-reading, saving snapshot, writing...";
                var svc = new CodingCenterService(owner); string orig = CodingEditing.ToHex(_orig);
                var o = await CodingWriteFlow.RunAsync(_vin, orig, target,
                    async () => (await Task.Run(() => svc.Read5FCodingAsync(null))).CodingHex ?? "",
                    async () => { await _history.AddSnapshotAsync(_vin, orig, _orig.Length, "Before write", p, ""); },
                    t => Task.Run(() => svc.Write5FCodingAsync(t, null)));
                Status.Text = "";
                if (o.VerifiedReadback) ShowResult(0, "STORED AND READ BACK", "The new coding was saved in the unit's storage and reading it back gives exactly the new value.", "This does NOT mean the unit is using it yet. Check the change on the unit. If you do not see it, restart the unit. This app has not verified whether that is needed.");
                else if (!o.Written) ShowResult(1, "NOT WRITTEN", o.Message.Replace("Nothing was written: ", "").Replace("Nothing was written.", "").Trim(), "Nothing on the unit was changed. Fix the reason above, then read the coding again.");
                else ShowResult(2, "NOT CONFIRMED", o.Message, "The unit may hold a different coding than intended. Do not write again. Tap Read from unit and check the Raw tab first; the snapshot in History is the old value.");
                if (o.VerifiedReadback) { _orig = CodingEditing.FromHex(o.AfterHex); _cur = (byte[])_orig.Clone(); }
                try { _snaps = await _history.LoadSnapshotsAsync(_vin); } catch { }
                        });
        if (res == GuardResult.Declined) ShowResult(1, "NOT WRITTEN", "The CODING confirmation was cancelled or not typed exactly.", "Nothing on the unit was changed.");
        else if (res != GuardResult.Done) ShowResult(1, "NOT WRITTEN", ConfirmedAction.Describe(res, "written"), "Nothing on the unit was changed. Check Write mode and the SSH connection, then read the coding again.");
        Refresh();
    }
}
