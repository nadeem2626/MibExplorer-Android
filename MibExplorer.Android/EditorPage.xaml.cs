using MibExplorer.Services;
using MibExplorer.Services.Editing;

namespace MibExplorer.Android;

public partial class EditorPage : ContentPage
{
    private const long MaxBytes = 1_000_000;
    private readonly string _path;
    private string _orig = ""; private bool _bom; private string _eol = "\n";
    private bool _ws, _loaded;
    private readonly bool _local;

    /// <summary>Edits a file on this device (Custom scripts). No unit connection or Write mode needed.</summary>
    public EditorPage(string localPath, bool local) : this(localPath) { _local = local; Title = "Edit script"; }

    public EditorPage(string remotePath)
    {
        InitializeComponent(); _path = remotePath; PathLbl.Text = remotePath;
        Session.WriteModeChanged += () => MainThread.BeginInvokeOnMainThread(UpdateSave);
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_loaded || (!_local && Session.Svc is null)) return; _loaded = true;
        if (!_local) _svcE0 = Session.Svc;
        try
        {
            Status.Text = "Downloading...";
            var tmp = Path.Combine(FileSystem.CacheDirectory, "edit_" + Guid.NewGuid().ToString("N")[..8]);
            if (_local) File.Copy(_path, tmp, true); else await Task.Run(() => _svcE0!.DownloadFileAsync(_path, tmp));
            var fi = new FileInfo(tmp);
            if (fi.Length > MaxBytes) { Status.Text = $"Too large to edit here ({fi.Length} bytes, limit {MaxBytes})."; Ed.IsReadOnly = true; return; }
            var data = File.ReadAllBytes(tmp); File.Delete(tmp);
            if (TextDiff.LooksBinary(data)) { Status.Text = "This looks like a binary file. Not opened as text."; Ed.IsReadOnly = true; return; }
            _origHash = System.Security.Cryptography.SHA256.HashData(data);
            var d = TextDiff.Decode(data); _orig = d.Text; _bom = d.Bom; _eol = d.Eol;
            Ed.Text = d.Text; Status.Text = $"{data.Length} bytes, {(d.Eol == "\n" ? "LF" : "CRLF")}{(d.Bom ? ", BOM" : "")}. " + (_local || Session.WriteMode ? "" : "Read only until Write mode is on (Settings).");
        }
        catch (Exception ex) { Status.Text = "Open failed: " + ex.Message; Ed.IsReadOnly = true; }
        UpdateSave();
    }

    private bool Changed => Ed.Text != _orig;
    private void OnChanged(object? s, TextChangedEventArgs e) => UpdateSave();
    private void UpdateSave() => SaveBtn.IsEnabled = _loaded && !Ed.IsReadOnly && Changed && (_local || (Session.WriteMode && Session.Protocol != "FTP"));
    private bool _inv, _col = true; private int _pos = -1; private readonly List<View> _marks = new();
    private void OnEdit(object? s, EventArgs e) { Ed.IsVisible = true; DiffScroll.IsVisible = false; DiffBar.IsVisible = false; }
    private void OnInv(object? s, EventArgs e) { _inv = !_inv; InvBtn.Text = "Invisibles: " + (_inv ? "on" : "off"); ShowDiff(); }
    private void OnCol(object? s, EventArgs e) { _col = !_col; ColBtn.Text = "Collapse: " + (_col ? "on" : "off"); ShowDiff(); }
    private async void GoTo(int i)
    {
        if (_marks.Count == 0) return; _pos = ((i % _marks.Count) + _marks.Count) % _marks.Count; PosLbl.Text = $"{_pos + 1}/{_marks.Count}";
        try { await DiffScroll.ScrollToAsync(_marks[_pos], ScrollToPosition.Start, true); } catch { }
    }
    private void OnNextDiff(object? s, EventArgs e) => GoTo(_pos + 1);
    private void OnPrevDiff(object? s, EventArgs e) => GoTo(_pos < 0 ? _marks.Count - 1 : _pos - 1);
    static string Vis(string t) => t.Replace(" ", "\u00B7").Replace("\t", "\u2192");
    private void OnWs(object? s, EventArgs e) { _ws = !_ws; WsBtn.Text = "Ignore spaces: " + (_ws ? "on" : "off"); if (DiffScroll.IsVisible) ShowDiff(); }
    private void OnDiff(object? s, EventArgs e) { ShowDiff(); }

    private void ShowDiff()
    {
        Ed.IsVisible = false; DiffScroll.IsVisible = true; DiffBar.IsVisible = true; DiffBody.Children.Clear(); _marks.Clear();
        var d = TextDiff.Compute(_orig, Ed.Text ?? "", _ws);
        int changed = TextDiff.ChangedCount(d);
        Status.Text = changed == 0 ? "No differences." : $"{changed} changed line(s).";
        bool Near(int k, int r) { for (int x = Math.Max(0, k - r); x <= Math.Min(d.Count - 1, k + r); x++) if (d[x].Kind != DiffKind.Same) return true; return false; }
        bool prevChanged = false;
        for (int i = 0; i < d.Count; i++)
        {
            var l = d[i];
            if (_col && l.Kind == DiffKind.Same && !Near(i, 2))
            {
                int j2 = i; while (j2 < d.Count && d[j2].Kind == DiffKind.Same && !Near(j2, 2)) j2++;
                DiffBody.Children.Add(new Label { Text = $"   ... {j2 - i} unchanged line(s) hidden ...", FontSize = 11, TextColor = Theme.C("Dim") });
                i = j2 - 1; prevChanged = false; continue;
            }
            string num = (l.OldLine?.ToString() ?? "").PadLeft(4) + " " + (l.NewLine?.ToString() ?? "").PadLeft(4) + " ";
            string txt = _inv ? Vis(l.Text) : l.Text;
            View row = l.Kind switch
            {
                DiffKind.Added => Row("+", num + txt, "DiffAddFg", "DiffAddBg"),
                DiffKind.Removed => Row("-", num + txt, "DiffDelFg", "DiffDelBg"),
                _ => Row(" ", num + txt, "Muted2", null)
            };
            DiffBody.Children.Add(row);
            bool ch = l.Kind != DiffKind.Same;
            if (ch && !prevChanged) _marks.Add(row);
            prevChanged = ch;
        }
        _pos = -1; PosLbl.Text = _marks.Count == 0 ? "0/0" : $"-/{_marks.Count}";
    }

    private static View Row(string mark, string text, string fg, string? bg) =>
        new Label { Text = mark + " " + text, FontFamily = "monospace", FontSize = 11, TextColor = Theme.C(fg), BackgroundColor = bg is null ? Colors.Transparent : Theme.C(bg), LineBreakMode = LineBreakMode.CharacterWrap };

    private byte[]? _origHash;
    private IMibConnectionService? _svcE0; // the connection this file was read from; read, hash check and save all use it
    private async void OnSave(object? s, EventArgs ev0) => await UiSafe.Run(() => OnSave_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnSave_Impl(object? s, EventArgs e)
    {
        if (_local)
        {
            AtomicFile.WriteAllBytes(_path, TextDiff.Encode(Ed.Text ?? "", _bom, _eol)); _orig = Ed.Text ?? ""; Status.Text = "Saved on this device."; UpdateSave(); return;
        }
        if (Session.Svc is null || !Session.WriteMode || !Changed) return;
        if (!ReferenceEquals(_svcE0, Session.Svc)) { Status.Text = "The connection changed since this file was opened. Nothing was saved. Reopen the file from the current connection."; return; }
        var d = TextDiff.Compute(_orig, Ed.Text ?? "");
        var guard = await ConfirmedAction.RunAsync<IMibConnectionService>(() => Session.Svc, () => ReferenceEquals(_svcE0, Session.Svc) && Session.WriteMode,
            () => DisplayAlert("Save to unit", $"{TextDiff.ChangedCount(d)} changed line(s) will be written to {_path} on {Session.Host}. A copy of the original is kept on this device first.", "Save", "Cancel"),
            async owner =>
            {
        try
        {
            // Optimistic check: the file on the unit must still be byte-identical to what was opened.
            Status.Text = "Checking that the file on the unit did not change...";
            var chkTmp = Path.Combine(FileSystem.CacheDirectory, "chk_" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                await Task.Run(() => _svcE0!.DownloadFileAsync(_path, chkTmp));
                var nowHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(chkTmp));
                if (_origHash is null || !nowHash.SequenceEqual(_origHash)) { Status.Text = "The file on the unit changed since you opened it. Nothing was saved. Reopen it to see the current version."; return; }
            }
            finally { try { File.Delete(chkTmp); } catch { } }
            string dir = Path.Combine(FileSystem.AppDataDirectory, "edit-backups"); Directory.CreateDirectory(dir);
            string bak = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd_HHmmss_") + Path.GetFileName(_path));
            File.WriteAllBytes(bak, TextDiff.Encode(_orig, _bom, _eol));
            string tmp = Path.Combine(FileSystem.CacheDirectory, "save_" + Guid.NewGuid().ToString("N")[..8]);
            File.WriteAllBytes(tmp, TextDiff.Encode(Ed.Text ?? "", _bom, _eol));
            Status.Text = "Saving...";
            if (!ReferenceEquals(_svcE0, Session.Svc) || !Session.WriteMode) { Status.Text = "The connection or Write mode changed during the check. Nothing was saved."; return; }
            var svcE = _svcE0!; string pathE = _path;
            var job = await Transfers.Queue.RunAsync("Save", pathE, async ct => { await Task.Run(() => SafeOps.ReplaceAsync(svcE, tmp, pathE, ct)); return ""; });
            if (job.State != TransferState.Done) throw new InvalidOperationException("Save " + job.State.ToString().ToLower() + ": " + job.Detail);
            File.Delete(tmp);
            _orig = Ed.Text ?? ""; _origHash = System.Security.Cryptography.SHA256.HashData(TextDiff.Encode(_orig, _bom, _eol)); Status.Text = "Saved. Original kept at " + bak; UpdateSave();
        }
        catch (Exception ex) { Status.Text = "Save failed: " + ex.Message; }
            });
        if (guard != GuardResult.Done && guard != GuardResult.Declined) Status.Text = ConfirmedAction.Describe(guard, "saved");
    }
}
