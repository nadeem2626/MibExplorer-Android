using MibExplorer.Services;
using System.Text;
using MibExplorer.Models.Adaptations;
using MibExplorer.Services.Adaptations;

namespace MibExplorer.Android;

public partial class AdaptationsPage : ContentPage
{
    public sealed record Row(string Title, string Sub, AdaptationCatalogGroup? Group, AdaptationCatalogItem? Item);

    private AdaptationCatalog? _cat;
    private AdaptationCatalogGroup? _open;
    private AdaptationsCenterService? _svc;
    private string _vin = "UNKNOWN";
    private object? _boundSvc;
    private bool Stale() { if (_svc is null || !ReferenceEquals(_boundSvc, Session.Svc)) { Status.Text = "The connection changed (reconnected or closed). Read the unit identity again first."; return true; } return false; }
    private readonly AdaptationHistoryService _hist = new();
    private readonly Dictionary<string, string> _raw = new();
    private readonly AdaptationBatch _batch = new();
    private void PendRefresh() => PendBtn.Text = $"Pending changes ({_batch.Items.Count})";

    private double _w, _h; private bool _drill;
    protected override void OnSizeAllocated(double width, double height) { base.OnSizeAllocated(width, height); if (width > 0) _w = width; if (height > 0) _h = height; ApplyLayout(); }
    private void ApplyLayout()
    {
        if (_w <= 0) return;
        switch (PaneLayout.Choose(_w, _h))
        {
            case PaneMode.Side:
                List.IsVisible = true; DetailCard.IsVisible = true; BackBtn.IsVisible = false; Grid.SetColumnSpan(List, 1);
                Grid.SetRow(DetailCard, 2); Grid.SetColumn(DetailCard, 1); Grid.SetColumnSpan(DetailCard, 1); DetailCard.HeightRequest = -1; break;
            case PaneMode.Stacked:
                List.IsVisible = true; DetailCard.IsVisible = true; BackBtn.IsVisible = false; Grid.SetColumnSpan(List, 2);
                Grid.SetRow(DetailCard, 3); Grid.SetColumn(DetailCard, 0); Grid.SetColumnSpan(DetailCard, 2); DetailCard.HeightRequest = PaneLayout.StackedHeight(_h); break;
            default:
                List.IsVisible = !_drill; DetailCard.IsVisible = _drill; BackBtn.IsVisible = _drill; Grid.SetColumnSpan(List, 2);
                Grid.SetRow(DetailCard, 2); Grid.SetColumn(DetailCard, 0); Grid.SetColumnSpan(DetailCard, 2); DetailCard.HeightRequest = -1; break;
        }
    }
    private void OnBack(object? s, EventArgs e) { _drill = false; ApplyLayout(); }

    public AdaptationsPage() { InitializeComponent(); AdaptationHistoryService.RootOverride = FileSystem.AppDataDirectory; }

    private async void OnIdentify(object? s, EventArgs e)
    {
        if (Session.UnitWritesLocked) { Status.Text = Session.LockText; return; }
        if (Session.Svc is null) { Status.Text = "Not connected"; return; }
        if (Session.Protocol != "SSH") { Status.Text = "Adaptations need an SSH connection."; return; }
        try
        {
            IdBtn.IsEnabled = false; Status.Text = "Reading identity...";
            _svc = new AdaptationsCenterService(Session.Svc); _boundSvc = Session.Svc; _batch.Discard(); PendRefresh(); _raw.Clear(); _cat = null; _open = null; List.ItemsSource = null; Detail.ItemsSource = null; DetailTitle.Text = "Select a group"; _vin = "UNKNOWN";
            string vin = await Task.Run(() => _svc.ReadVinAsync()); _vin = vin;
            var (id, ver) = await Task.Run(() => _svc.ReadAsamIdentityAsync());
            _cat = new AdaptationCatalogService().LoadForAsam(id, ver);
            if (_cat is null) { Status.Text = $"VIN {vin}. ASAM {id} / {ver}: no catalog for this version."; return; }
            bool anyEdit = _cat.Groups.Any(g => g.Adaptations.Any(a => a.Ui.Editable && a.Storage.Mapped));
            PendBtn.IsVisible = anyEdit;
            bool anyMapped = _cat.Groups.Any(g => g.Adaptations.Any(a => a.Storage.Mapped));
            Intro.Text = anyEdit
                ? "Identifies the unit, loads the matching catalog and reads stored values. Changing a value needs SSH, Write mode, a preflight that passes, and typing ADAPT. The old value is saved to History first."
                : anyMapped
                    ? "Identifies the unit, loads the matching catalog and reads stored values. This catalog is read only: no adaptation is marked as changeable, so nothing can be staged or written from here. Earlier history entries stay viewable."
                    : "Identifies the unit and shows the matching catalog as a reference list. None of these adaptations is linked to a storage location on the unit, so values cannot be read or changed here. Earlier history entries stay viewable.";
            Status.Text = $"VIN {vin}  ·  ASAM {id}  ODX {ver}  ·  Catalog: {_cat.Name} ({_cat.Groups.Count} groups)" + (anyEdit ? "" : anyMapped ? "  ·  Read only catalog" : "  ·  Reference list only: no values are read from the unit");
            ShowGroups();
        }
        catch (Exception ex) { Status.Text = "Failed: " + ex.Message; }
        finally { IdBtn.IsEnabled = true; }
    }

    private void ShowGroups()
    {
        if (_cat is null) return;
        string q = Search.Text?.Trim() ?? "";
        List<Row> rows;
        if (q.Length > 0)
            rows = _cat.Groups.SelectMany(g => g.Adaptations.Where(a => a.Label.Contains(q, StringComparison.OrdinalIgnoreCase) || g.Label.Contains(q, StringComparison.OrdinalIgnoreCase)).Select(a => ItemRow(g, a))).Take(300).ToList();
        else
            rows = _cat.Groups.OrderBy(g => g.Order).Select(g => new Row(g.Label, $"{g.Adaptations.Count} adaptations  {g.Rdid}", g, null)).ToList();
        List.ItemsSource = rows;
    }

    private Row ItemRow(AdaptationCatalogGroup g, AdaptationCatalogItem a)
    {
        string flags = (a.Ui.Editable ? "editable" : "read only") + (a.Storage.Mapped ? ", mapped" : ", not mapped") + (a.Ui.Dangerous ? ", dangerous" : "");
        string val = _raw.TryGetValue(a.Id, out var v) ? "  |  value: " + AdaptationDecoder.Decode(a, v) : "";
        return new Row(a.Label, g.Label + "  |  " + flags + val, g, a);
    }

    private void OnSearch(object? s, TextChangedEventArgs e) => ShowGroups();

    private void ShowItems(AdaptationCatalogGroup g)
    {
        _open = g;
        DetailTitle.Text = $"{g.Label}  {g.Rdid}";
        var rows = new List<Row>();
        if (g.Adaptations.Any(a => a.Storage.Mapped && a.Storage.PhysicalKeys.Count > 0)) rows.Add(new("Read values for this group", "Read only. Nothing is written.", g, null));
        rows.AddRange(g.Adaptations.OrderBy(a => a.Order).Select(a => ItemRow(g, a)));
        Detail.ItemsSource = rows;
    }

    private async void OnPick(object? s, SelectionChangedEventArgs ev0) => await UiSafe.Run(() => OnPick_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnPick_Impl(object? s, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Row r) return;
        if (r.Item is not null) { List.SelectedItem = null; ShowItems(r.Group!); _drill = true; ApplyLayout(); await ItemMenu(r.Item, r.Group); return; }
        if (r.Group is null) return;
        ShowItems(r.Group); // group stays highlighted; its adaptations fill the detail pane
        _drill = true; ApplyLayout();
    }

    private async void OnPickDetail(object? s, SelectionChangedEventArgs ev0) => await UiSafe.Run(() => OnPickDetail_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnPickDetail_Impl(object? s, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Row r) return;
        Detail.SelectedItem = null;
        if (r.Item is not null) { await ItemMenu(r.Item, r.Group); return; }
        if (r.Group is not null && r.Title.StartsWith("Read values")) await ReadGroup(r.Group);
    }

    private async void OnHistory(object? s, EventArgs e)
    {
        try
        {
            var list = (await _hist.LoadEntriesAsync(_vin)).Take(30).ToList();
            if (list.Count == 0) { await DisplayAlert("History", "No saved adaptation changes for " + _vin + ". Read the unit identity first if the VIN shows UNKNOWN.", "OK"); return; }
            string? pick = await this.Sheet("History for " + _vin, "Close", null, list.Select(x => x.CreatedAtDisplay + "  " + x.Summary).ToArray());
            var en = list.FirstOrDefault(x => pick == x.CreatedAtDisplay + "  " + x.Summary); if (en is null) return;
            await DisplayAlert(en.CreatedAtDisplay, $"Key {en.PhysicalKey}\nSource: {en.Source}\nStatus: {en.Status}\nBefore: {en.OriginalRawValue}\nWritten: {en.MergedRawValue}\nRead back: {(string.IsNullOrEmpty(en.ReadbackRawValue) ? "-" : en.ReadbackRawValue)}", "OK");
            if (!Session.WriteMode || Session.Protocol != "SSH") return;
            string? act = await this.Sheet("Restore the Before value of this entry?", "Not now", null, "Restore the Before value");
            if (act == "Restore the Before value") await RestoreAsync(en);
        }
        catch (Exception ex) { Status.Text = "History failed: " + ex.Message; }
    }

    private async Task ItemMenu(AdaptationCatalogItem a, AdaptationCatalogGroup? g)
    {
        bool canEdit = a.Ui.Editable && a.Storage.Mapped && a.Storage.PhysicalKeys.Count == 1 && _raw.ContainsKey(a.Id) && Session.WriteMode && Session.Protocol == "SSH";
        bool hasChoices = a.Ui.Values.Count > 0 || a.Ui.Min is not null || a.Ui.Max is not null;
        var opts = new List<string> { "Details" };
        if (hasChoices) opts.Add("Choices (view only)");
        if (canEdit) { opts.Add("Stage change (apply later with others)"); opts.Add("Change this one now"); }
        string? pick = await this.Sheet(a.Label, "Close", null, opts.ToArray());
        if (pick == "Details") { await DisplayAlert(a.Label, Describe(a), "OK"); return; }
        if (pick == "Choices (view only)") { await DisplayAlert(a.Label, Choices(a), "OK"); return; }
        if (pick is null || !(pick.StartsWith("Stage") || pick.StartsWith("Change this"))) return;
        var labels = a.Ui.Values.Where(v => !string.Equals(v.Label, "Reset selection", StringComparison.OrdinalIgnoreCase)).Select(v => v.Label).ToArray();
        if (labels.Length == 0) { await DisplayAlert(a.Label, "No selectable values in the catalog for this item.", "OK"); return; }
        string? choice = await this.Sheet("New value for " + a.Label, "Cancel", null, labels);
        if (string.IsNullOrEmpty(choice) || choice == "Cancel") return;
        if (pick.StartsWith("Stage"))
        {
            if (Stale()) return;
            string? err = _batch.Stage(a, choice, _raw[a.Id]);
            Status.Text = err ?? $"Staged: {a.Label} > {choice}. Nothing is written until you apply the pending changes."; PendRefresh(); return;
        }
        await ApplyChange(a, g, choice);
    }

    private async void OnPending(object? s, EventArgs ev0) => await UiSafe.Run(() => OnPending_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnPending_Impl(object? s, EventArgs e)
    {
        if (_batch.Items.Count == 0) { await DisplayAlert("Pending changes", "Nothing staged. Open an adaptation and choose Stage change.", "OK"); return; }
        var b = _batch.Build();
        string body = b.Error ?? string.Join("\n", b.Lines);
        string? act = await this.Sheet($"{_batch.Items.Count} staged:\n{body}", "Close", null, "Apply all (preflight, then ADAPT)", "Discard all");
        if (act == "Discard all") { _batch.Discard(); PendRefresh(); Status.Text = "Pending changes discarded. Nothing was written."; return; }
        if (act is null || !act.StartsWith("Apply")) return;
        if (b.Error != null) { Status.Text = b.Error; return; }
        if (b.Transactions.Count == 0) { Status.Text = "The staged values already match the unit. Nothing to write."; _batch.Discard(); PendRefresh(); return; }
        bool ok = await CommitManyAsync(b.Transactions, string.Join("\n", b.Lines), null, "Android: before write (batch)");
        if (ok) { _batch.Discard(); PendRefresh(); }
    }

    private async Task ApplyChange(AdaptationCatalogItem a, AdaptationCatalogGroup? g, string choice)
    {
        if (_svc is null || Session.Svc is null || Stale()) return;
        if (!Session.WriteMode) { Status.Text = "Turn on Write mode in Settings first."; return; }
        if (Session.Protocol != "SSH") { Status.Text = "Adaptation writes need an SSH connection. The connection changed since this page opened."; return; }
        var key = a.Storage.PhysicalKeys[0]; string cur = _raw[a.Id];
        var plan = AdaptationEncoder.Plan(a, cur, choice);
        if (!plan.Ok) { await DisplayAlert("Cannot change", plan.Error, "OK"); return; }
        var tx = AdaptationEncoder.Transaction(key, cur, plan.NewRaw);
        if (!tx.IsDirty) { await DisplayAlert("No change", "The stored value already matches.", "OK"); return; }
        await CommitAsync(tx, $"{a.Label}\n{AdaptationDecoder.Decode(a, cur)}  >  {choice}", g, "Android: before write");
    }

    private async Task CommitAsync(PhysicalWriteTransaction tx, string headline, AdaptationCatalogGroup? g, string source)
    {
        bool ok = await CommitManyAsync(new List<PhysicalWriteTransaction> { tx }, headline, g, source);
    }

    private async Task<bool> CommitManyAsync(List<PhysicalWriteTransaction> txs, string headline, AdaptationCatalogGroup? g, string source)
    {
        if (_svc is null || Session.Svc is null || Stale()) return false;
        if (!Session.WriteMode) { Status.Text = "Turn on Write mode in Settings first."; return false; }
        if (Session.Protocol != "SSH") { Status.Text = "Adaptation writes need an SSH connection."; return false; }
        foreach (var t in txs) t.Changes.Clear();
        bool ok = false;
        try
        {
            Status.Text = "Preflight on the unit (checks the current values, writes nothing)...";
            var pre = await Task.Run(() => _svc.PreflightWriteAdaptationsAsync(txs));
            if (!pre.Success) { Status.Text = "Preflight failed: " + pre.Message; await DisplayAlert("Preflight failed", pre.Message + "\nNothing was written.", "OK"); return false; }
        }
        catch (Exception ex) { Status.Text = "Preflight failed: " + ex.Message; return false; }
        string raws = string.Join("\n", txs.Select(t => $"Raw {t.PhysicalKey.Partition}:{t.PhysicalKey.Key}  {t.OriginalRawValue.Trim()}  >  {t.MergedRawValue}"));
        string msg = $"{headline}\n{raws}\n\nPreflight passed. The current values are saved to History first. This writes {txs.Count} key(s) to {Session.Host}. Type ADAPT to confirm.";
        if (txs.Count > 1) msg += "\n\nNOT ATOMIC: the keys are written one by one. If one fails, the others can still be written and nothing is rolled back automatically. The old values are saved in History; restoring is manual, one entry at a time.";
        var res = await ConfirmedAction.RunAsync<object>(
            () => ReferenceEquals(_boundSvc, Session.Svc) ? Session.Svc : null,
            () => Session.WriteMode && Session.Protocol == "SSH",
            async () => string.Equals((await DisplayPromptAsync("Write adaptation", msg, "Write", "Cancel"))?.Trim(), "ADAPT", StringComparison.Ordinal),
            async owner =>
            {
                try { await _hist.AddTransactionsAsync(_vin, source, txs); }
                catch (Exception ex) { Status.Text = "Nothing was written: the History entry could not be saved first: " + ex.Message; return; }
                try
                {
                    Status.Text = "Writing...";
                    var r = await Task.Run(() => _svc.WriteAdaptationsAsync(txs));
                    Status.Text = (r.Success ? "Write reported success. " : "Write failed. ") + r.Message;
                    ok = r.Success;
                    if (r.Success && g is not null) { await ReadGroup(g); }
                }
                catch (Exception ex) { Status.Text = "Write failed or its result is unknown: " + ex.Message + " Read the values again before doing anything else."; }
            });
        if (res != GuardResult.Done && res != GuardResult.Declined) Status.Text = ConfirmedAction.Describe(res, "written");
        else if (res == GuardResult.Declined) Status.Text = "Cancelled. Nothing was written.";
        return ok;
    }

    private async Task RestoreAsync(AdaptationHistoryEntry en)
    {
        if (_svc is null || Session.Svc is null || Stale()) return;
        if (!Session.WriteMode) { Status.Text = "Turn on Write mode in Settings first."; return; }
        if (Session.Protocol != "SSH") { Status.Text = "Adaptation writes need an SSH connection."; return; }
        var key = AdaptationRestore.ParseKey(en.PhysicalKey);
        if (key is null) { Status.Text = "This entry cannot be restored: unreadable key."; return; }
        try
        {
            Status.Text = "Reading the current value from the unit...";
            var def = new AdaptationDefinition { Id = "restore", Ecu = "5F", Group = "restore", Label = en.PhysicalKey, StorageMode = "scalar", PhysicalKeys = new() { key }, Persistence = new AdaptationPersistence { Partition = key.Partition, Key = key.Key, Type = key.Type } };
            var vals = await Task.Run(() => _svc.ReadAdaptationsAsync(new[] { def }));
            string live = vals.FirstOrDefault(v => v.Partition == key.Partition && v.Key == key.Key)?.Value ?? "";
            var (tx, err) = AdaptationRestore.Plan(en, live);
            if (tx is null) { Status.Text = err; await DisplayAlert("Restore", err, "OK"); return; }
            await CommitAsync(tx, "Restore the value saved on " + en.CreatedAtDisplay, null, "Android: restore");
        }
        catch (Exception ex) { Status.Text = "Restore failed: " + ex.Message; }
    }

    private string Choices(AdaptationCatalogItem a)
    {
        var sb = new StringBuilder();
        string? cur = _raw.TryGetValue(a.Id, out var rv) ? AdaptationDecoder.Decode(a, rv) : null;
        sb.AppendLine(a.Ui.Editable && a.Storage.Mapped ? "Values listed in the catalog:" : "Values listed in the catalog. View only: this item cannot be changed in this app.");
        foreach (var v in a.Ui.Values.Where(v => !string.Equals(v.Label, "Reset selection", StringComparison.OrdinalIgnoreCase)))
            sb.AppendLine((cur is not null && string.Equals(v.Label, cur, StringComparison.Ordinal) ? "> " : "   ") + v.Label);
        if (a.Ui.Min is not null || a.Ui.Max is not null) sb.AppendLine($"Range: {a.Ui.Min} to {a.Ui.Max} {a.Unit}");
        sb.AppendLine(cur is null ? "Current value: not read yet (open the group and choose Read values)." : "Current value (read from the unit): " + cur + "   (marked >)");
        return sb.ToString();
    }

    private string Describe(AdaptationCatalogItem a)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id: " + a.Id);
        sb.AppendLine("Editor: " + a.Ui.Editor + (a.Ui.Editable ? " (editable in catalog)" : " (read only)"));
        sb.AppendLine("Storage: " + (a.Storage.Mapped ? "mapped, " + a.Storage.Mode : "not mapped"));
        foreach (var k in a.Storage.PhysicalKeys) sb.AppendLine($"Key {k.Partition}:{k.Key}:{k.Type}");
        if (a.Ui.Values.Count > 0) sb.AppendLine("Values: " + string.Join(", ", a.Ui.Values.Take(12).Select(v => v.Label)));
        if (a.Ui.Min is not null || a.Ui.Max is not null) sb.AppendLine($"Range: {a.Ui.Min} to {a.Ui.Max} {a.Unit}");
        if (_raw.TryGetValue(a.Id, out var rv)) { sb.AppendLine("Stored raw: " + (rv.Length > 200 ? rv[..200] + "..." : rv)); sb.AppendLine("Decoded: " + AdaptationDecoder.Decode(a, rv)); }
        if (a.Ui.Dangerous) sb.AppendLine("Marked dangerous in the catalog.");
        if (a.Ui.LimitedWriteCount) sb.AppendLine("Limited write count.");
        if (!string.IsNullOrWhiteSpace(a.Ui.Reason)) sb.AppendLine(a.Ui.Reason);
        return sb.ToString();
    }

    private async Task ReadGroup(AdaptationCatalogGroup g)
    {
        if (_svc is null || Stale()) return;
        try
        {
            Status.Text = "Reading " + g.Label + "...";
            var defs = g.Adaptations.Where(a => a.Storage.Mapped && a.Storage.PhysicalKeys.Count > 0).Select(a => new AdaptationDefinition
            {
                Id = a.Id, Ecu = "5F", Group = g.Label, Label = a.Label, StorageMode = a.Storage.Mode,
                PhysicalKeys = a.Storage.PhysicalKeys.ToList(),
                Persistence = new AdaptationPersistence { Partition = a.Storage.PhysicalKeys[0].Partition, Key = a.Storage.PhysicalKeys[0].Key, Type = a.Storage.PhysicalKeys[0].Type },
            }).ToList();
            if (defs.Count == 0) { Status.Text = g.Label + ": no adaptation in this group is linked to a storage location, so nothing was read from the unit."; ShowItems(g); return; }
            var vals = await Task.Run(() => _svc.ReadAdaptationsAsync(defs));
            var map = vals.ToDictionary(v => v.CacheKey, v => v.Value);
            foreach (var d in defs)
            {
                var k = d.PhysicalKeys[0];
                if (map.TryGetValue(AdaptationReadValue.BuildCacheKey(k.Partition, k.Key, k.Type), out var v)) _raw[d.Id] = v;
            }
            Status.Text = $"Read {map.Count} stored values for {g.Label} (decoded with the catalog; raw value is in the item details)."; ShowItems(g);
        }
        catch (Exception ex) { Status.Text = "Read failed: " + ex.Message; }
    }
}
