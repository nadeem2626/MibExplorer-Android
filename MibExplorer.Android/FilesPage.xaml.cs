using MibExplorer.Models;
using MibExplorer.Services;

namespace MibExplorer.Android;

public partial class FilesPage : ContentPage, IThemeAware
{
    public sealed class Row : MibExplorer.Core.ObservableObject
    {
        public static bool WideColumns = true, SelectMode; // layout state shared by all rows, applied by ApplyLayout
        public Row(string name, string path, bool isDir, string icon, string sizeLabel, RemoteExplorerItem item) { Name = name; Path = path; IsDir = isDir; Icon = icon; SizeLabel = sizeLabel; Item = item; Modified = LsDate.Label(item.ModifiedAt); }
        public string Name { get; } public string Path { get; } public bool IsDir { get; } public string Icon { get; } public string SizeLabel { get; } public string Modified { get; } public RemoteExplorerItem Item { get; }
        private bool _sel;
        public bool Selected { get => _sel; set { if (SetProperty(ref _sel, value)) { OnPropertyChanged(nameof(CheckMark)); OnPropertyChanged(nameof(CheckBg)); OnPropertyChanged(nameof(Bg)); } } }
        public bool ShowCheck => SelectMode;
        public bool ShowModified => WideColumns;
        public double CheckOpacity => 1; // folders can be marked (Move, Delete if empty); Download and Copy act on files only
        public string CheckMark => _sel ? "\u2713" : "";
        public Color CheckBg => _sel ? Theme.C("Accent") : Colors.Transparent;
        public Color Bg => _sel ? Theme.C("SelTint") : Colors.Transparent;
        public void ThemeChanged() { OnPropertyChanged(nameof(CheckBg)); OnPropertyChanged(nameof(Bg)); }
        public void LayoutChanged() { OnPropertyChanged(nameof(ShowCheck)); OnPropertyChanged(nameof(ShowModified)); }
    }
    private List<Row> _rows = new();
    public void OnThemeChanged() { foreach (var r in _rows) r.ThemeChanged(); }

    public FilesPage()
    {
        InitializeComponent();
        SizeChanged += (_, __) => ApplyLayout();
        Session.WriteModeChanged += () => MainThread.BeginInvokeOnMainThread(RefreshMode);
    }

    // The Modified column needs room: hidden on narrow windows (portrait phones), shown otherwise. Rows are not rebuilt, so selection survives rotation.
    private bool _tablet;
    private void ApplyLayout()
    {
        bool wide = RowLayout.ShowModified(Width);
        // narrow (portrait phone): breadcrumb on its own line, actions below it
        Grid.SetRow(BrowseActions, wide ? 0 : 1); Grid.SetColumn(BrowseActions, wide ? 1 : 0); Grid.SetColumnSpan(BrowseActions, wide ? 1 : 2); BrowseActions.HorizontalOptions = wide ? LayoutOptions.End : LayoutOptions.Start;
        bool tablet = Width >= 700; // primary controls directly in the top bar; the overflow keeps only the secondary upload variants
        _tablet = tablet; foreach (var b in new[] { UpBtn, RefreshBtn, SelectBtn, HiddenBtn, NewFolderBtn }) b.IsVisible = tablet;
        if (wide == Row.WideColumns && HeadModified.IsVisible == wide) return;
        Row.WideColumns = wide; HeadModified.IsVisible = wide;
        foreach (var r in _rows) r.LayoutChanged();
    }

    private void RefreshMode()
    {
        bool w = Session.WriteMode && Session.IsConnected;
        UploadBtn.Opacity = w || _selectMode ? 1 : 0.55; // stays tappable: read-only taps explain why nothing happens
        _modeNote = "";
        Note.Text = string.IsNullOrEmpty(_navNote) ? _modeNote : _navNote;
        FooterLabel.Text = w ? "Write mode: ON for this session" : "Write mode: OFF (read-only)"; FooterLabel.TextColor = Theme.C(w ? "Accent2" : "Dim");
        UpdateBanner();
    }

    // The design's "System area" banner: shown when nothing can be changed here (Write mode off, or a path this app never writes under).
    private void UpdateBanner()
    {
        string p = WriteGuard.Normalize(Session.Path ?? "/");
        bool protectedArea = WriteGuard.IsProtectedArea(p), w = Session.WriteMode && Session.IsConnected;
        SysBanner.IsVisible = Session.IsConnected && (protectedArea || !w);
        SysBannerText.Text = protectedArea ? "System area. Read-only. This app never writes under /proc or /dev (except /dev/shmem), even with Write mode on."
            : "System area. Read-only. Turn on Write mode in Settings to change anything here.";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing(); RefreshMode();
        if (Session.Svc is null) { Crumbs.Children.Clear(); Crumbs.Children.Add(PathLabel); PathLabel.Text = "Not connected"; FileList.ItemsSource = null; StatusLabel.Text = "Open the Connect tab first."; return; }
        await ListAsync(Session.Path ?? "/");
    }

    // // the folder being opened right now, so Up/Open pressed during a slow listing build on it
    private string _modeNote = "", _navNote = "";
    private void SetNav(string t) { _navNote = t; Note.Text = string.IsNullOrEmpty(t) ? _modeNote : t; }
    private readonly ListNavigator<IMibConnectionService> _nav = new();
    private readonly ListingSelection<IMibConnectionService, Row> _selection = new();
    private IReadOnlyList<RemoteExplorerItem> _items = Array.Empty<RemoteExplorerItem>(); private string _itemsPath = "/";
    private SortKey _sortKey = SortKey.Name; private bool _sortDesc;

    // Rows come from the raw listing through the tested Core rules (hidden filter, name filter, sort). Rebuilding marks nothing: the selection is rebound and empty.
    private void BuildRows(IMibConnectionService svc, string path)
    {
        var visible = _items.Where(i => _showHidden || !i.Name.StartsWith("."));
        var shown = FileListView.Sort(FileListView.Filter(visible, SearchEntry.Text), _sortKey, _sortDesc);
        var rows = shown.Select(i => new Row(i.Name, i.FullPath, i.IsDirectory, i.IsDirectory ? "ic_folder.png" : "ic_file.png", i.IsDirectory ? "" : i.SizeLabel, i)).ToList();
        FileList.ItemsSource = rows; _rows = rows;
        _selection.Bind(svc, path);
        bool filtered = !string.IsNullOrWhiteSpace(SearchEntry.Text);
        StatusLabel.Text = filtered ? $"{rows.Count} of {_items.Count} items match" : _items.Count + " items";
    }

    private void BuildCrumbs(string path)
    {
        Crumbs.Children.Clear();
        Crumbs.Children.Add(new Label { Text = "unit", TextColor = Theme.C("Muted"), VerticalOptions = LayoutOptions.Center });
        var list = FileListView.Breadcrumb(path);
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i]; bool last = i == list.Count - 1;
            Crumbs.Children.Add(new Label { Text = " \u203A ", TextColor = Theme.C("Muted"), VerticalOptions = LayoutOptions.Center });
            var l = new Label { Text = c.Label, VerticalOptions = LayoutOptions.Center, Padding = new Thickness(4, 10), FontAttributes = last ? FontAttributes.Bold : FontAttributes.None, TextColor = last ? Theme.C("Text") : Theme.C("Muted") };
            if (!last) { var t = new TapGestureRecognizer(); string target = c.Path; t.Tapped += async (_, __) => await ListAsync(target); l.GestureRecognizers.Add(t); }
            Crumbs.Children.Add(l);
        }
        MainThread.BeginInvokeOnMainThread(async () => { try { await Task.Delay(50); await CrumbScroll.ScrollToAsync(Crumbs, ScrollToPosition.End, false); } catch { } });
    }

    private void OnSort(object? s, EventArgs e)
    {
        // Name, Modified and Size in turn. Same key again is not offered; direction flips every full cycle.
        var next = FileListView.NextKey(_sortKey); if (next == SortKey.Name) _sortDesc = !_sortDesc; _sortKey = next;
        SortBtn.Text = "Sort: " + _sortKey + (_sortDesc ? " (desc)" : "");
        if (Session.Svc is not null && ReferenceEquals(_listSvc, Session.Svc)) BuildRows(Session.Svc, _itemsPath);
    }
    private void OnSearchToggle(object? s, EventArgs e)
    {
        SearchEntry.IsVisible = !SearchEntry.IsVisible;
        if (!SearchEntry.IsVisible) { SearchEntry.Text = ""; } else SearchEntry.Focus();
        SearchBtn.Text = SearchEntry.IsVisible ? "Close search" : "Search";
    }
    private void OnSearchChanged(object? s, TextChangedEventArgs e) { if (Session.Svc is not null && ReferenceEquals(_listSvc, Session.Svc)) BuildRows(Session.Svc, _itemsPath); }

    private async Task ListAsync(string path)
    {
        var svc = Session.Svc; if (svc is null) return;
        if (_nav.DisplayedConn is not null && !ReferenceEquals(_nav.DisplayedConn, svc)) { _nav.Reset(); _listSvc = null; } // nothing of the old connection may steer Up
        var ticket = _nav.Begin(path, svc);
        try
        {
            StatusLabel.Text = "Loading..."; SetNav("Opening " + path + " ...");
            var items = await svc.GetChildrenAsync(path);
            if (!_nav.Complete(ticket, Session.Svc)) return; // newer listing or another connection took over; this result changes neither path nor selection
            Session.Path = path; _listSvc = svc; SetNav("");
            _items = items; _itemsPath = path; BuildCrumbs(path);
            BuildRows(svc, path);
            ApplyLayout(); UpdateBanner();
        }
        catch (Exception ex)
        {
            if (_nav.Fail(ticket)) { SetNav("Could not open " + path + ": " + ex.Message + " (still showing " + (Session.Path ?? "/") + ")"); StatusLabel.Text = "List error: " + ex.Message; }
        }
    }

    private bool _showHidden = true;
    private async void OnHidden(object? s, EventArgs e) { _showHidden = !_showHidden; await ListAsync(Session.Path ?? "/"); }
    private async void OnRefresh(object? s, EventArgs e) => await ListAsync(Session.Path ?? "/");
    private async void OnUp(object? s, EventArgs e)
    {
        string? up = _nav.UpTarget();
        if (up is null) { SetNav("Already at the top (" + (Session.Path ?? "/") + ")."); return; }
        await ListAsync(up);
    }

    /// <summary>"-rwxr-xr--" to "754" (with setuid/setgid/sticky digit when present). Display only.</summary>
    public static string Octal(string mode)
    {
        if (mode.Length < 10) return "?";
        int Triplet(int o) => (mode[o] == 'r' ? 4 : 0) + (mode[o + 1] == 'w' ? 2 : 0) + ("xsStT".IndexOf(mode[o + 2]) is 0 or 1 or 3 ? 1 : 0);
        int special = (mode[3] is 's' or 'S' ? 4 : 0) + (mode[6] is 's' or 'S' ? 2 : 0) + (mode[9] is 't' or 'T' ? 1 : 0);
        var core = $"{Triplet(1)}{Triplet(4)}{Triplet(7)}";
        return special == 0 ? core : special + core;
    }

    private async void OnRowTapped(object? s, TappedEventArgs ev0) => await Safe(() => OnRowTapped_Impl(s, ev0));
    private async Task OnRowTapped_Impl(object? s, TappedEventArgs e)
    {
        if ((s as BindableObject)?.BindingContext is not Row r || Session.Svc is null) return;
        if (_selectMode) { r.Selected = _selection.Toggle(r); SelCount.Text = _selection.Count + " selected"; StatusLabel.Text = ""; return; }
        if (r.IsDir) { await ListAsync(r.Path); return; }
        await ShowMenu(r);
    }

    private async void OnRowMenu(object? s, EventArgs ev0) => await Safe(() => OnRowMenu_Impl(s, ev0));
    private async Task OnRowMenu_Impl(object? s, EventArgs e)
    {
        if (_selectMode) return;
        if ((s as BindableObject)?.BindingContext is Row r && Session.Svc is not null) await ShowMenu(r);
    }

    /// <summary>async-void handlers must never let an exception escape (it would crash the app). Report it on the status line instead.</summary>
    private async Task Safe(Func<Task> f)
    {
        try { await f(); }
        catch (Exception ex) { try { StatusLabel.Text = "Error: " + ex.Message; } catch { } }
    }
    private IMibConnectionService? _listSvc; // the connection the visible list came from
    private async Task ShowMenu(Row r)
    {
        if (!ReferenceEquals(_listSvc, Session.Svc)) { StatusLabel.Text = "The connection changed. This list is from a previous connection. Refreshing."; await ListAsync(Session.Path ?? "/"); return; }
        var menuSvc = Session.Svc; // the connection this menu belongs to; checked again after the sheet closes
        bool w = Session.WriteMode;
        string suf = w ? "" : " (needs Write mode)";
        var opts = new List<string>();
        if (r.IsDir) { opts.Add("Open"); opts.Add("Download folder (zip)"); if (Session.CanTerminal && !Session.UnitWritesLocked) opts.Add("Open in Shell"); }
        if (!r.IsDir) { opts.Add("Download"); opts.Add("View / edit text"); }
        opts.Add("Details");
        opts.Add("Permissions (chmod)" + (w ? "" : " - view only"));
        if (!r.IsDir && Session.Protocol != "FTP") opts.Add("Replace with file..." + suf);
        if (r.IsDir) opts.Add("Replace folder with folder..." + suf);
        if (!r.IsDir) opts.Add("Copy to..." + suf);
        opts.Add("Move to..." + suf);
        opts.Add("Rename..." + suf); opts.Add("Delete..." + suf);
        var choice = await this.Sheet(r.Name, "Cancel", null, opts.ToArray());
        if (choice is null || choice == "Cancel") return;
        if (menuSvc is null || !ReferenceEquals(Session.Svc, menuSvc)) { StatusLabel.Text = "The connection changed while the menu was open. Nothing was done. Refreshing."; await ListAsync(Session.Path ?? "/"); return; }
        if (choice == "Open") await ListAsync(r.Path);
        else if (choice == "Download") await DownloadAsync(r);
        else if (choice == "Open in Shell") await Navigation.PushAsync(new TerminalPage(menuSvc, r.Path));
        else if (choice.StartsWith("Download folder")) await DownloadFolderAsync(r);
        else if (choice == "View / edit text") await Navigation.PushAsync(new EditorPage(r.Path));
        else if (choice == "Details") await Navigation.PushAsync(new DetailsPage(menuSvc, r.Item));
        else if (choice.StartsWith("Permissions")) await Navigation.PushAsync(new ChmodPage(r.Item));
        else if (!w) { StatusLabel.Text = "Turn on Write mode in Settings first."; }
        else if (choice.StartsWith("Replace folder")) await ReplaceFolderUiAsync(r);
        else if (choice.StartsWith("Replace")) await ReplaceAsync(r);
        else if (choice.StartsWith("Copy to")) await CopyMoveAsync(new List<Row> { r }, false);
        else if (choice.StartsWith("Move to")) await CopyMoveAsync(new List<Row> { r }, true);
        else if (choice.StartsWith("Rename")) await RenameAsync(r);
        else if (choice.StartsWith("Delete")) await DeleteAsync(r);
    }

    private async Task DownloadAsync(Row r)
    {
        try
        {
            var local = Path.Combine(FileSystem.AppDataDirectory, "downloads", Path.GetFileName(r.Path));
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);
            StatusLabel.Text = "Downloading " + r.Name + "...";
            var svcD = Session.Svc!;
            var job = await Transfers.Queue.RunAsync("Download", r.Path, async (ct, pr) => { await svcD.DownloadFileAsync(r.Path, local, pr, ct); return new FileInfo(local).Length + " bytes"; });
            if (job.State != TransferState.Done) { try { if (File.Exists(local)) File.Delete(local); } catch { } StatusLabel.Text = "Download " + job.State.ToString().ToLower() + ": " + job.Detail; await AfterInterruptedAsync(svcD, r.Path); return; }
            StatusLabel.Text = "Downloaded: " + r.Name;
            await Share.Default.RequestAsync(new ShareFileRequest { Title = "Save " + r.Name, File = new ShareFile(local) });
        }
        catch (Exception ex) { StatusLabel.Text = "Download error: " + ex.Message; }
    }

    private async Task DownloadFolderAsync(Row r)
    {
        try
        {
            string tmp = Path.Combine(FileSystem.CacheDirectory, "fdl_" + Guid.NewGuid().ToString("N")[..6]);
            StatusLabel.Text = "Downloading folder " + r.Name + "...";
            var prog = new Progress<string>(p => StatusLabel.Text = "Downloading " + p);
            SafeOps.FolderResult? res0 = null; var svcF = Session.Svc!;
            var job = await Transfers.Queue.RunAsync("Folder download", r.Path, async ct => { res0 = await Task.Run(() => SafeOps.DownloadFolderAsync(svcF, r.Path, Path.Combine(tmp, r.Name), 5000, prog, ct)); return $"{res0.Files} files, {res0.Bytes} bytes, {res0.Skipped.Count} skipped"; });
            if (job.State != TransferState.Done || res0 is null) { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { } StatusLabel.Text = "Folder download " + job.State.ToString().ToLower() + ": " + job.Detail; await AfterInterruptedAsync(svcF, r.Path); return; }
            var res = res0;
            string zip = Path.Combine(FileSystem.AppDataDirectory, "downloads", r.Name + ".zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zip)!); if (File.Exists(zip)) File.Delete(zip);
            System.IO.Compression.ZipFile.CreateFromDirectory(Path.Combine(tmp, r.Name), zip, System.IO.Compression.CompressionLevel.Optimal, true);
            Directory.Delete(tmp, true);
            StatusLabel.Text = $"Folder downloaded: {res.Files} files, {res.Folders} folders, {res.Bytes} bytes" + (res.Skipped.Count > 0 ? $", {res.Skipped.Count} skipped (symlinks/unsafe)" : "");
            await Share.Default.RequestAsync(new ShareFileRequest { Title = "Save " + r.Name + ".zip", File = new ShareFile(zip) });
        }
        catch (Exception ex) { StatusLabel.Text = "Folder download error: " + ex.Message; }
    }

    private static string Join(string dir, string name) => (dir == "/" ? "" : dir.TrimEnd('/')) + "/" + name;

    /// <summary>FTP and Telnet close the link when a transfer is cancelled (seen against independent servers). Say so, and name the temp file that may be left on the unit.</summary>
    private async Task AfterInterruptedAsync(IMibConnectionService svc, string path)
    {
        bool alive = await svc.ProbeConnectionAsync(TimeSpan.FromSeconds(3));
        if (alive) return;
        if (!Session.ClearIfCurrent(svc)) { StatusLabel.Text += " (This failure belongs to a previous connection. The current connection was left alone.)"; return; }
        Session.SetStatus("Disconnected (the interrupted transfer closed the link)");
        StatusLabel.Text += " The connection closed. Reconnect from the Connect tab. A temporary file whose name starts with .mibx-up- (or ends with .mibx-part) may be left in that folder on the unit. Check it by hand.";
    }

    private async Task<bool> Write(string label, string path, Func<IMibConnectionService, CancellationToken, Task> op, bool wrap = true)
    {
        var svc = Session.Svc; if (svc is null || !Session.WriteMode) return false;
        if (!ReferenceEquals(_listSvc, svc)) { StatusLabel.Text = "The connection changed since this list was loaded. Nothing was changed. Refresh first."; return false; }
        StatusLabel.Text = label + "...";
        // Rename, Replace and Upload already do their own mount handling in the SSH service; wrapping them again would deadlock.
        var job = await Transfers.Queue.RunAsync(label.StartsWith("Upload") ? "Upload" : label.StartsWith("Replace") ? "Replace" : "Change", path, async qct =>
        {
            if (wrap) await Task.Run(() => svc.RunWritableOperationAsync(path, ct => op(svc, ct), qct));
            else await Task.Run(() => op(svc, qct));
            return "";
        });
        if (job.State == TransferState.Done) { StatusLabel.Text = label + ": done"; await ListAsync(Session.Path ?? "/"); return true; }
        StatusLabel.Text = label + " " + job.State.ToString().ToLower() + ": " + job.Detail; await AfterInterruptedAsync(svc, path); return false;
    }

    private async void OnNewFolder(object? s, EventArgs ev0) => await Safe(() => OnNewFolder_Impl(s, ev0));
    private async Task OnNewFolder_Impl(object? s, EventArgs e)
    {
        string? name = await DisplayPromptAsync("New folder", "Name (no slash):", "Create", "Cancel");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (name.Contains('/') || name is "." or ".." || name.Any(char.IsControl)) { StatusLabel.Text = "Invalid folder name"; return; }
        string p = Join(Session.Path ?? "/", name);
        await Write("New folder", p, (svc, ct) => SafeOps.MakeDirectoryAsync(svc, p, ct), wrap: false);
    }

    private async Task<string?> PickToCacheAsync()
    {
        var pick = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a file" });
        if (pick is null) return null;
        var tmp = Path.Combine(FileSystem.CacheDirectory, "up_" + Guid.NewGuid().ToString("N")[..6] + "_" + pick.FileName);
        using (var src = await pick.OpenReadAsync()) using (var dst = File.Create(tmp)) await src.CopyToAsync(dst);
        return tmp;
    }

    private async Task ReplaceFolderUiAsync(Row r)
    {
        string? tmp = null;
        try
        {
#if ANDROID
            if (Session.Svc is null) return;
            var copy = await MibExplorer.Android.FolderCopier.PickAndCopyAsync(300L << 20, CancellationToken.None);
            if (copy is null) return;
            tmp = copy.LocalDir;
            if (!await DisplayAlert("Replace folder?", $"{copy.Files} files will be uploaded to {r.Path}.mibx-new. Then {r.Path} is renamed to {r.Name}.mibx-old and the new folder takes its place. The old folder is kept, nothing is deleted. If you cancel or the link drops, a partial .mibx-new folder may stay on the unit.", "Replace", "Cancel")) return;
            var svc = Session.Svc; var prog = new Progress<string>(p => StatusLabel.Text = "Uploading " + p); string dir = tmp; string path = r.Path;
            var job = await Transfers.Queue.RunAsync("Folder replace", path, async ct => { var rr = await SafeOps.ReplaceFolderAsync(svc, dir, path, prog, ct); return $"{rr.Upload.Files} files. Old folder kept as {rr.BackupPath}"; });
            StatusLabel.Text = job.State == TransferState.Done ? "Replaced. " + job.Detail : "Folder replace " + job.State.ToString().ToLower() + ": " + job.Detail + " Check the unit for " + r.Name + ".mibx-new / .mibx-old. Nothing was rolled back.";
            await ListAsync(Session.Path ?? "/");
#endif
        }
        catch (Exception ex) { StatusLabel.Text = "Folder replace failed: " + ex.Message; }
        finally { try { if (tmp != null && Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { } }
    }

    private async void OnUploadDir(object? s, EventArgs ev0) => await Safe(() => OnUploadDir_Impl(s, ev0));
    private async Task OnUploadDir_Impl(object? s, EventArgs e)
    {
        string? tmp = null;
        try
        {
            if (!Session.WriteMode || Session.Svc is null) { StatusLabel.Text = "Turn on Write mode first."; return; }
#if ANDROID
            var copy = await MibExplorer.Android.FolderCopier.PickAndCopyAsync(300L << 20, CancellationToken.None);
            if (copy is null) return;
            tmp = copy.LocalDir;
            string? name = await DisplayPromptAsync("New remote folder", $"{copy.Files} files ({copy.Bytes >> 10} KB) found. Name of the NEW folder to create in " + (Session.Path ?? "/") + ". It must not exist yet. Existing folders are never merged.", "Upload", "Cancel", initialValue: copy.PickedName);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim(); WriteGuard.RequireName(name);
            string target = Join(Session.Path ?? "/", name);
            if (await Session.Svc.RemotePathExistsAsync(target)) { StatusLabel.Text = target + " already exists. Pick another name."; return; }
            if (copy.Notes.Count > 0 && !await DisplayAlert("Names changed", copy.Notes.Count + " item(s) were renamed or skipped to stay safe on the unit:\n" + string.Join("\n", copy.Notes.Take(8)), "Continue", "Cancel")) return;
            var svc = Session.Svc; var prog = new Progress<string>(p => StatusLabel.Text = "Uploading " + p); string dir = tmp;
            var job = await Transfers.Queue.RunAsync("Folder upload", target, async ct => { var r = await SafeOps.UploadFolderAsync(svc, dir, target, prog, ct); return $"{r.Files} files, {r.Folders} folders, {r.Bytes} bytes" + (r.Skipped.Count > 0 ? ", " + r.Skipped.Count + " skipped" : ""); });
            StatusLabel.Text = job.State == TransferState.Done ? "Uploaded " + job.Detail : "Folder upload " + job.State.ToString().ToLower() + ": " + job.Detail + " The new folder may be partly there on the unit. Nothing was rolled back.";
            await ListAsync(Session.Path ?? "/");
#endif
        }
        catch (Exception ex) { StatusLabel.Text = "Folder upload failed: " + ex.Message; }
        finally { try { if (tmp != null && Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { } }
    }

    private async void OnUploadMany(object? s, EventArgs ev0) => await Safe(() => OnUploadMany_Impl(s, ev0));
    private async Task OnUploadMany_Impl(object? s, EventArgs e)
    {
        string? tmp = null;
        try
        {
            if (!Session.WriteMode || Session.Svc is null) return;
            string? name = await DisplayPromptAsync("New remote folder", "Name of the NEW folder to create in " + (Session.Path ?? "/") + ". It must not exist yet. Files are never merged into an existing folder.", "Next", "Cancel");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim(); WriteGuard.RequireName(name);
            string target = Join(Session.Path ?? "/", name);
            if (await Session.Svc.RemotePathExistsAsync(target)) { StatusLabel.Text = target + " already exists. Pick another name."; return; }
            var picks = (await FilePicker.Default.PickMultipleAsync(new PickOptions { PickerTitle = "Choose files for the new folder" }))?.ToList();
            if (picks is null || picks.Count == 0) return;
            var map = UploadNameMap.Build(picks.Select(p => p.FileName));
            int changed = map.Count(m => m.Changed);
            string preview = $"{map.Count} files go into {target}.\n" + (changed > 0 ? $"{changed} name(s) are changed to stay safe on the unit:\n" + string.Join("\n", map.Where(m => m.Changed).Take(8).Select(m => m.Original + "  >  " + m.Safe)) : "No names change.");
            if (!await DisplayAlert("Upload files", preview, "Upload", "Cancel")) return;
            tmp = Path.Combine(FileSystem.CacheDirectory, "upm_" + Guid.NewGuid().ToString("N")[..6]); Directory.CreateDirectory(tmp);
            for (int i = 0; i < picks.Count; i++) using (var src = await picks[i].OpenReadAsync()) using (var dst = File.Create(Path.Combine(tmp, map[i].Safe))) await src.CopyToAsync(dst);
            var svc = Session.Svc; var prog = new Progress<string>(p => StatusLabel.Text = "Uploading " + p); string dir = tmp;
            SafeOps.FolderResult? res = null;
            var job = await Transfers.Queue.RunAsync("Folder upload", target, async ct => { res = await SafeOps.UploadFolderAsync(svc, dir, target, prog, ct); return $"{res.Files} files, {res.Bytes} bytes" + (changed > 0 ? $", {changed} renamed" : ""); });
            StatusLabel.Text = job.State == TransferState.Done ? "Uploaded " + job.Detail : "Folder upload " + job.State.ToString().ToLower() + ": " + job.Detail + (job.State != TransferState.Done ? " The new folder may exist partly filled." : "");
            await ListAsync(Session.Path ?? "/");
        }
        catch (Exception ex) { StatusLabel.Text = "Upload failed: " + ex.Message; }
        finally { try { if (tmp != null && Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { } }
    }

    private bool _selectMode;
    private void OnSelectToggle(object? s, EventArgs e)
    {
        _selectMode = !_selectMode;
        foreach (var r in _rows) r.Selected = false; _selection.Clear(); Row.SelectMode = _selectMode; foreach (var r in _rows) r.LayoutChanged();
        SelectBar.IsVisible = _selectMode; BrowseBar.IsVisible = !_selectMode; SelCount.Text = "0 selected";
        UploadBtn.Text = _selectMode ? "Cancel" : "+ Upload"; UploadBtn.Opacity = _selectMode || (Session.WriteMode && Session.IsConnected) ? 1 : 0.55;
        StatusLabel.Text = _selectMode ? "" : "Select mode off.";
    }

    private async void OnDownloadSelected(object? s, EventArgs ev0) => await Safe(() => OnDownloadSelected_Impl(s, ev0));
    private async Task OnDownloadSelected_Impl(object? s, EventArgs e)
    {
        var svc = Session.Svc; if (svc is null) return;
        if (!ReferenceEquals(_listSvc, svc)) { StatusLabel.Text = "The connection changed since this list was loaded. Nothing downloaded. Refresh first."; return; }
        var taken = _selection.Take(svc, Session.Path ?? "/"); if (taken is null) { StatusLabel.Text = "The selection belongs to another folder or connection. Nothing downloaded. Refresh and select again."; return; }
        var files = taken.Where(r => !r.IsDir).ToList(); int skipped = taken.Count - files.Count;
        if (files.Count == 0) { StatusLabel.Text = "Nothing selected. Tap files first."; return; }
        // Remove this app's own old selection folders (older than a day) so they do not pile up. Only sel_* folders this feature created.
        try { var root = Path.Combine(FileSystem.AppDataDirectory, "downloads"); if (Directory.Exists(root)) foreach (var d in Directory.GetDirectories(root, "sel_*")) if (Directory.GetCreationTimeUtc(d) < DateTime.UtcNow.AddDays(-1)) Directory.Delete(d, true); } catch { }
        var shared = new List<ShareFile>(); var failed = new List<string>();
        string dir = Path.Combine(FileSystem.AppDataDirectory, "downloads", "sel_" + Guid.NewGuid().ToString("N")[..6]); Directory.CreateDirectory(dir);
        var names = UploadNameMap.Build(files.Select(f => f.Name));
        for (int i = 0; i < files.Count; i++)
        {
            var r = files[i]; string local = Path.Combine(dir, names[i].Safe);
            StatusLabel.Text = $"Downloading {i + 1}/{files.Count}: {r.Name}";
            var job = await Transfers.Queue.RunAsync("Download", r.Path, async (ct, pr) => { await svc.DownloadFileAsync(r.Path, local, pr, ct); return new FileInfo(local).Length + " bytes"; });
            if (job.State == TransferState.Done) shared.Add(new ShareFile(local));
            else { failed.Add(r.Name); try { if (File.Exists(local)) File.Delete(local); } catch { } if (!ReferenceEquals(Session.Svc, svc) || !await svc.ProbeConnectionAsync(TimeSpan.FromSeconds(3))) { await AfterInterruptedAsync(svc, r.Path); break; } }
        }
        StatusLabel.Text = $"{shared.Count} of {files.Count} downloaded" + (skipped > 0 ? $" ({skipped} folder(s) ignored, use Download folder)" : "") + (failed.Count > 0 ? ", failed: " + string.Join(", ", failed.Take(5)) : "") + ".";
        if (shared.Count > 0) { try { await Share.Default.RequestAsync(new ShareMultipleFilesRequest { Title = "Save " + shared.Count + " files", Files = shared }); } catch (Exception ex) { StatusLabel.Text += " Share failed: " + ex.Message; } }
    }

    private async void OnUpload(object? s, EventArgs ev0) => await Safe(() => OnUpload_Impl(s, ev0));
    private async Task OnUpload_Impl(object? s, EventArgs e)
    {
        var tmps = new List<string>();
        try
        {
            if (!Session.WriteMode || Session.Svc is null) return;
            var picks = (await FilePicker.Default.PickMultipleAsync(new PickOptions { PickerTitle = "Choose one or more files to upload here" }))?.ToList();
            if (picks is null || picks.Count == 0) return;
            // Bind the whole batch to the connection and folder that are current right after the pick; if either changes, the batch stops.
            var batchSvc = Session.Svc; string batchPath = Session.Path ?? "/";
            if (batchSvc is null || !Session.WriteMode || !ReferenceEquals(_listSvc, batchSvc)) { StatusLabel.Text = "The connection or Write mode changed while choosing. Nothing uploaded."; return; }
            var map = UploadNameMap.Build(picks.Select(p => p.FileName));
            var targets = map.Select(m => Join(batchPath, m.Safe)).ToList();
            var existing = new List<string>();
            foreach (var t in targets) if (await batchSvc.RemotePathExistsAsync(t)) existing.Add(t);
            if (existing.Count > 0) { StatusLabel.Text = "Nothing uploaded. Already on the unit (use Replace on each): " + string.Join(", ", existing.Take(5).Select(System.IO.Path.GetFileName)) + (existing.Count > 5 ? "..." : ""); return; }
            int changed = map.Count(m => m.Changed);
            string preview = $"{picks.Count} file(s) go into {Session.Path ?? "/"}:\n" + string.Join("\n", map.Take(10).Select(m => m.Changed ? m.Original + "  >  " + m.Safe : m.Safe)) + (picks.Count > 10 ? "\n..." : "") + "\nNo existing file is overwritten. If one fails, the ones before it stay uploaded (no rollback).";
            if (!await DisplayAlert("Upload files here", preview, "Upload", "Cancel")) return;
            int ok = 0;
            for (int i = 0; i < picks.Count; i++)
            {
                if (!ReferenceEquals(Session.Svc, batchSvc) || !Session.WriteMode || (Session.Path ?? "/") != batchPath) { StatusLabel.Text = $"Stopped after {ok} of {picks.Count}: the connection, folder or Write mode changed."; return; }
                var tmp = Path.Combine(FileSystem.CacheDirectory, "up_" + Guid.NewGuid().ToString("N")[..6]); tmps.Add(tmp);
                using (var src = await picks[i].OpenReadAsync()) using (var dst = File.Create(tmp)) await src.CopyToAsync(dst);
                string target = targets[i];
                bool good = await Write($"Upload {i + 1}/{picks.Count} {map[i].Safe}", target, (svc, ct) => SafeOps.UploadAsync(svc, tmp, target, ct), wrap: false);
                if (!good) { StatusLabel.Text += $" Stopped after {ok} of {picks.Count} uploaded."; return; }
                ok++;
            }
            StatusLabel.Text = $"Uploaded {ok} file(s)." + (changed > 0 ? $" {changed} name(s) were changed to stay safe." : "");
        }
        catch (Exception ex) { StatusLabel.Text = "Upload failed: " + ex.Message; }
        finally { foreach (var t in tmps) try { File.Delete(t); } catch { } }
    }

    private async Task ReplaceAsync(Row r)
    {
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose the new file for " + r.Name });
            if (pick is null) return;
            if (!await DisplayAlert("Replace file?", $"{r.Path} will be replaced by {pick.FileName}.", "Replace", "Cancel")) return;
            var tmp = Path.Combine(FileSystem.CacheDirectory, "rep_" + Guid.NewGuid().ToString("N")[..6]);
            using (var src = await pick.OpenReadAsync()) using (var dst = File.Create(tmp)) await src.CopyToAsync(dst);
            await Write("Replace " + r.Name, r.Path, (svc, ct) => SafeOps.ReplaceAsync(svc, tmp, r.Path, ct), wrap: false);
            File.Delete(tmp);
        }
        catch (Exception ex) { StatusLabel.Text = "Replace failed: " + ex.Message; }
    }

    private async Task RenameAsync(Row r)
    {
        string? name = await DisplayPromptAsync("Rename", "New name (no slash):", "Rename", "Cancel", initialValue: r.Name);
        if (string.IsNullOrWhiteSpace(name) || name == r.Name) return;
        name = name.Trim();
        if (name.Contains('/') || name is "." or ".." || name.Any(char.IsControl)) { StatusLabel.Text = "Invalid name"; return; }
        await Write("Rename", r.Path, (svc, ct) => SafeOps.RenameAsync(svc, r.Path, name, ct), wrap: false);
    }

    private async Task DeleteAsync(Row r)
    {
        string msg = r.IsDir ? $"Delete the empty folder {r.Path}? Folders that still contain files are refused (no recursive delete)." : $"Delete {r.Path}? This cannot be undone.";
        if (!await DisplayAlert("Delete", msg, "Delete", "Cancel")) return;
        await Write("Delete", r.Path, (svc, ct) => SafeOps.DeleteAsync(svc, r.Item, ct), wrap: false);
    }

    // ---- multi-select actions: all bound to the connection and folder the selection was made in ----
    private List<Row>? TakeSelection(string verb, bool needsWrite)
    {
        var svc = Session.Svc; if (svc is null) return null;
        if (needsWrite && !Session.WriteMode) { StatusLabel.Text = "Turn on Write mode in Settings first."; return null; }
        if (!ReferenceEquals(_listSvc, svc)) { StatusLabel.Text = "The connection changed since this list was loaded. Nothing was " + verb + ". Refresh first."; return null; }
        var taken = _selection.Take(svc, Session.Path ?? "/");
        if (taken is null) { StatusLabel.Text = "The selection belongs to another folder or connection. Nothing was " + verb + ". Refresh and select again."; return null; }
        if (taken.Count == 0) { StatusLabel.Text = "Nothing selected."; return null; }
        return taken.ToList();
    }

    /// <summary>Re-checked after every await that waits for the user (picker, confirmation): connection, listed folder, list owner and Write mode must be unchanged.</summary>
    private bool StillValid(IMibConnectionService svc, string folder, string verb)
    {
        if (!ReferenceEquals(Session.Svc, svc) || !ReferenceEquals(_listSvc, svc) || (Session.Path ?? "/") != folder) { StatusLabel.Text = "The connection or folder changed while you were choosing. Nothing was " + verb + "."; return false; }
        if (!Session.WriteMode) { StatusLabel.Text = "Write mode was turned off. Nothing was " + verb + "."; return false; }
        return true;
    }

    private void EndSelectMode() { if (_selectMode) OnSelectToggle(null, EventArgs.Empty); }

    private async void OnCopySelected(object? s, EventArgs ev0) => await Safe(() => OnCopySelected_Impl(s, ev0));
    private async Task OnCopySelected_Impl(object? s, EventArgs e) { var t = TakeSelection("copied", true); if (t != null) await CopyMoveAsync(t, false); }
    private async void OnMoveSelected(object? s, EventArgs ev0) => await Safe(() => OnMoveSelected_Impl(s, ev0));
    private async Task OnMoveSelected_Impl(object? s, EventArgs e) { var t = TakeSelection("moved", true); if (t != null) await CopyMoveAsync(t, true); }
    private async void OnRenameSelected(object? s, EventArgs ev0) => await Safe(() => OnRenameSelected_Impl(s, ev0));
    private async Task OnRenameSelected_Impl(object? s, EventArgs e)
    {
        var t = TakeSelection("renamed", true); if (t is null) return;
        if (t.Count != 1) { StatusLabel.Text = "Rename works on exactly one item. Select one."; return; }
        await RenameAsync(t[0]); EndSelectMode();
    }
    private async void OnDeleteSelected(object? s, EventArgs ev0) => await Safe(() => OnDeleteSelected_Impl(s, ev0));
    private async Task OnDeleteSelected_Impl(object? s, EventArgs e)
    {
        var t = TakeSelection("deleted", true); if (t is null) return;
        var dsvc = Session.Svc!; string dfolder = Session.Path ?? "/";
        string list = string.Join("\n", t.Take(10).Select(r => (r.IsDir ? "[folder] " : "") + r.Name)) + (t.Count > 10 ? $"\n... and {t.Count - 10} more" : "");
        if (!await DisplayAlert("Delete " + t.Count + " item(s)?", list + "\n\nFiles are removed for good. Folders are removed only when empty; others are refused and kept. This cannot be undone.", "Delete", "Cancel")) return;
        if (!StillValid(dsvc, dfolder, "deleted")) return;
        t = TakeSelection("deleted", true); if (t is null) return; // selection must still belong to this connection and folder
        await BatchAsync("Delete", t, (svc, r, ct) => SafeOps.DeleteAsync(svc, r.Item, ct));
        EndSelectMode();
    }

    private async Task CopyMoveAsync(List<Row> rows, bool move)
    {
        var svc = Session.Svc; if (svc is null) return;
        if (!Session.WriteMode) { StatusLabel.Text = "Turn on Write mode in Settings first."; return; }
        string folder = Session.Path ?? "/";
        if (!move && rows.Any(r => r.IsDir)) { StatusLabel.Text = "Copy works on files only. Folders are not copied (use Download folder or Move)."; rows = rows.Where(r => !r.IsDir).ToList(); if (rows.Count == 0) return; }
        string? dest = await PickFolderAsync(svc, Session.Path ?? "/", (move ? "Move " : "Copy ") + rows.Count + " item(s) to");
        if (dest is null) return;
        if (!StillValid(svc, folder, move ? "moved" : "copied")) return;
        string note = svc is FtpMibConnectionService && !move ? "\nOver FTP the bytes pass through this device (no server-side copy)." : "";
        if (!await DisplayAlert(move ? "Move" : "Copy", $"{rows.Count} item(s) go to {dest}.\nNothing existing is overwritten; clashes are skipped and reported." + note, move ? "Move" : "Copy", "Cancel")) return;
        if (!StillValid(svc, folder, move ? "moved" : "copied")) return;
        if (_selectMode) { var again = TakeSelection(move ? "moved" : "copied", true); if (again is null) return; var keep = rows.Select(r => r.Path).ToHashSet(); rows = again.Where(r => keep.Contains(r.Path)).ToList(); if (rows.Count == 0) return; }
        await BatchAsync(move ? "Move" : "Copy", rows, (c, r, ct) => move ? SafeOps.MoveAsync(c, r.Item, dest, ct) : SafeOps.CopyAsync(c, r.Item, dest, null, ct));
        EndSelectMode();
    }

    /// <summary>Folder chooser built on action sheets: shows the subfolders of the current folder, "Use this folder", and Up.</summary>
    private async Task<string?> PickFolderAsync(IMibConnectionService svc, string start, string title)
    {
        string cur = start;
        while (true)
        {
            if (!ReferenceEquals(Session.Svc, svc)) return null;
            IReadOnlyList<RemoteExplorerItem> kids;
            try { kids = await svc.GetChildrenAsync(cur); } catch (Exception ex) { StatusLabel.Text = "Cannot list " + cur + ": " + ex.Message; return null; }
            var all = kids.Where(k => k.IsDirectory && k.EntryType != RemoteEntryType.Symlink).OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase).ToList();
            const int Page = 40; int pages = Math.Max(1, (all.Count + Page - 1) / Page); int pageNo = 0; string? pick;
            while (true)
            {
                var dirs = all.Skip(pageNo * Page).Take(Page).ToList();
                var opts = new List<string> { "Use this folder" };
                if (cur != "/") opts.Add(".. (up)");
                if (pageNo > 0) opts.Add("< Previous folders");
                opts.AddRange(dirs.Select(d => d.Name + "/"));
                if (pageNo < pages - 1) opts.Add("More folders >");
                pick = await this.Sheet(title + "\n" + cur + (pages > 1 ? $"  (page {pageNo + 1}/{pages}, {all.Count} folders)" : ""), "Cancel", null, opts.ToArray());
                if (pick == "More folders >") { pageNo++; continue; }
                if (pick == "< Previous folders") { pageNo--; continue; }
                if (pick != null && pick != "Cancel" && pick != "Use this folder" && pick != ".. (up)") { var d = dirs.FirstOrDefault(x => x.Name + "/" == pick); if (d != null) cur = d.FullPath; }
                break;
            }
            if (pick is null || pick == "Cancel") return null;
            if (pick == "Use this folder") return cur;
            if (pick == ".. (up)") { cur = WriteGuard.Parent(WriteGuard.Normalize(cur)); continue; }
        }
    }

    /// <summary>Runs one operation per item, one at a time. Each item is its own change: a batch is NOT atomic, earlier items stay done if a later one fails. Reports exactly what happened.</summary>
    private async Task BatchAsync(string label, List<Row> rows, Func<IMibConnectionService, Row, CancellationToken, Task> op)
    {
        var svc = Session.Svc; if (svc is null) return;
        var ok = new List<string>(); var failed = new List<string>(); int notRun = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (!ReferenceEquals(Session.Svc, svc) || !Session.WriteMode) { notRun = rows.Count - i; break; }
            StatusLabel.Text = $"{label} {i + 1}/{rows.Count}: {r.Name}";
            var job = await Transfers.Queue.RunAsync("Change", r.Path, async qct => { await Task.Run(() => op(svc, r, qct), qct); return ""; });
            if (job.State == TransferState.Done) ok.Add(r.Name);
            else
            {
                failed.Add(r.Name + " (" + job.Detail + ")");
                if (job.State == TransferState.Cancelled) { notRun = rows.Count - i - 1; break; }
                if (!await svc.ProbeConnectionAsync(TimeSpan.FromSeconds(3))) { notRun = rows.Count - i - 1; await AfterInterruptedAsync(svc, r.Path); break; }
            }
        }
        StatusLabel.Text = $"{label}: {ok.Count} done, {failed.Count} failed" + (notRun > 0 ? $", {notRun} not run" : "") + ". Not atomic: finished items stay done." + (failed.Count > 0 ? " Failed: " + string.Join("; ", failed.Take(3)) : "");
        await ListAsync(Session.Path ?? "/");
    }

    // The design's single "+ Upload" button: uploads into the open folder, or leaves Select mode.
    private async void OnFab(object? s, EventArgs ev0) => await Safe(() => OnFab_Impl(s, ev0));
    private async Task OnFab_Impl(object? s, EventArgs e)
    {
        if (_selectMode) { OnSelectToggle(null, EventArgs.Empty); return; }
        if (Session.Svc is null) { StatusLabel.Text = "Open the Connect tab first."; return; }
        if (!Session.WriteMode) { StatusLabel.Text = "Read-only. Turn on Write mode in Settings to upload."; return; }
        OnUpload(s, e);
        await Task.CompletedTask;
    }

    // The design's overflow menu: everything that no longer has its own button.
    private async void OnOverflow(object? s, EventArgs ev0) => await Safe(() => OnOverflow_Impl(s, ev0));
    private async Task OnOverflow_Impl(object? s, EventArgs e)
    {
        bool w = Session.WriteMode && Session.IsConnected; string suf = w ? "" : " (needs Write mode)";
        var items = _tablet
            ? new[] { "Upload files as new folder" + suf, "Upload a folder (recursive)" + suf }
            : new[] { "Up", "Refresh", "Select", "Hidden files: " + (_showHidden ? "shown (tap to hide)" : "hidden (tap to show)"), "New folder" + suf, "Upload files as new folder" + suf, "Upload a folder (recursive)" + suf };
        var choice = await this.Sheet("Files", "Close", null, items);
        if (choice is null || choice == "Close") return;
        if (choice == "Up") OnUp(null, EventArgs.Empty);
        else if (choice == "Refresh") OnRefresh(null, EventArgs.Empty);
        else if (choice == "Select") OnSelectToggle(null, EventArgs.Empty);
        else if (choice.StartsWith("Hidden")) OnHidden(null, EventArgs.Empty);
        else if (!w) StatusLabel.Text = "Read-only. Turn on Write mode in Settings first.";
        else if (choice.StartsWith("New folder")) OnNewFolder(null, EventArgs.Empty);
        else if (choice.StartsWith("Upload files")) OnUploadMany(null, EventArgs.Empty);
        else if (choice.StartsWith("Upload a folder")) OnUploadDir(null, EventArgs.Empty);
    }
}
