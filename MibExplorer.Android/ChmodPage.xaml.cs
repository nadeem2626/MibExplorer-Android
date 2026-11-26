using MibExplorer.Models;
using MibExplorer.Services;

namespace MibExplorer.Android;

public partial class ChmodPage : ContentPage
{
    private readonly RemoteExplorerItem _item;
    private PermissionBits _orig;
    private PermissionBits _cur;
    private bool _sync;
    private readonly CheckBox[,] _c;

    public ChmodPage(RemoteExplorerItem item)
    {
        InitializeComponent();
        _item = item;
        _c = new[,] { { C00, C01, C02 }, { C10, C11, C12 }, { C20, C21, C22 } };
        PathLabel.Text = item.FullPath;
        OwnerLabel.Text = $"Owner {item.Owner}  Group {item.Group}  (shown as listed; cannot be changed here)";
        _orig = _cur = PermissionBits.FromLsMode(item.Mode) ?? new PermissionBits(0);
        Session.WriteModeChanged += () => MainThread.BeginInvokeOnMainThread(Update);
        Push(); Update();
    }

    private void Push()
    {
        _sync = true;
        for (int r = 0; r < 3; r++) for (int k = 0; k < 3; k++) _c[r, k].IsChecked = _cur.Get(r, k);
        OctalEntry.Text = _cur.ToOctal();
        _sync = false;
    }

    private void OnGrid(object? s, CheckedChangedEventArgs e)
    {
        if (_sync) return;
        var bits = _cur;
        for (int r = 0; r < 3; r++) for (int k = 0; k < 3; k++) bits = bits.With(r, k, _c[r, k].IsChecked);
        _cur = bits;
        _sync = true; OctalEntry.Text = _cur.ToOctal(); _sync = false; Update();
    }

    private void OnOctal(object? s, TextChangedEventArgs e)
    {
        if (_sync) return;
        if (PermissionBits.TryParseOctal(OctalEntry.Text, out var b)) { _cur = b; _sync = true; for (int r = 0; r < 3; r++) for (int k = 0; k < 3; k++) _c[r, k].IsChecked = b.Get(r, k); _sync = false; }
        Update();
    }

    private bool OctalValid => PermissionBits.TryParseOctal(OctalEntry.Text, out _);

    private void Update()
    {
        var perm = Session.Perm;
        SymLabel.Text = OctalValid ? _cur.ToSymbolic(_item.IsDirectory ? 'd' : '-') + "   " + _cur.ToOctal() : "Octal must be 3 or 4 digits, 0 to 7";
        Preview.Text = OctalValid ? "Will run: " + (Session.Protocol == "FTP" ? "SITE CHMOD " + _cur.ToOctal() + " " + _item.FullPath : ChmodCommand.Build(_cur, _item.FullPath)) : "";
        string? gate = null;
        if (perm is null || !perm.PermissionCapabilities.CanChmod) gate = "This connection cannot change permissions.";
        else if (!Session.WriteMode) gate = "Write mode is off. Turn it on in Settings to apply.";
        else if (!OctalValid) gate = "Fix the octal value first.";
        else if (_item.EntryType == RemoteEntryType.Symlink) gate = "This is a symlink: changing it would change its target, so it is not offered.";
        else if (_cur.Mode == _orig.Mode) gate = "No change yet.";
        Gate.Text = gate ?? perm!.PermissionCapabilities.Note;
        ApplyBtn.IsEnabled = gate is null;
        ApplyBtn.Opacity = gate is null ? 1 : 0.4;
    }

    private async void OnApply(object? s, EventArgs ev0) => await UiSafe.Run(() => OnApply_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnApply_Impl(object? s, EventArgs e)
    {
        var perm = Session.Perm;
        if (perm is null || !Session.WriteMode || !OctalValid) return;
        string sp = (_orig.Mode >> 9) != 0 && (_cur.Mode >> 9) == 0 ? "\n\nWarning: this removes the special bits (setuid/setgid/sticky)." : "";
        var svc0 = Session.Svc; var item = _item; var cur = _cur; var orig = _orig;
        var guard = await ConfirmedAction.RunAsync<IMibConnectionService>(() => Session.Svc, () => ReferenceEquals(svc0, Session.Svc) && Session.WriteMode,
            () => DisplayAlert("Change permissions?", $"{item.FullPath}\n{orig.ToOctal()} to {cur.ToOctal()}{sp}\n\nThe result is read back from the unit and checked.", "Apply", "Cancel"),
            async owner =>
            {
                try
                {
                    ApplyBtn.IsEnabled = false; Result.Text = "Applying...";
                    var actual = await Task.Run(() => SafeOps.ChmodAsync(owner, item, cur));
                    _orig = _cur = actual; Result.Text = "Done. The unit now reports " + actual.ToOctal() + ".";
                }
                catch (Exception ex) { Result.Text = "Failed: " + ex.Message; }
            });
        if (guard != GuardResult.Done && guard != GuardResult.Declined) Result.Text = ConfirmedAction.Describe(guard, "changed");
        Update();
    }
}
