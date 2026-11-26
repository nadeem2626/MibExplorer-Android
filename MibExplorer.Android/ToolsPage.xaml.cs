using MibExplorer.Services;
namespace MibExplorer.Android;

public partial class ToolsPage : ContentPage
{
    public ToolsPage() { InitializeComponent(); SizeChanged += (_, __) => SettingsTile.IsVisible = !AppShell.RailVisible; }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SettingsTile.IsVisible = !AppShell.RailVisible; // landscape: Settings is in the rail, so no duplicate tile
        Info.Text = !Session.IsConnected ? "Not connected. Scripts (list, import, update), SD packages, Transfers and Settings work without the unit. Terminal, running scripts, Reboot and Uninstall need a connection."
            : Session.Protocol == "SSH" ? "Connected over SSH: " + Session.Host
            : "Connected over " + Session.Protocol + ". Running scripts, Reboot and Uninstall need SSH and are refused on other protocols.";
    }

    private bool RequireSsh()
    {
        if (Session.Svc is null) { Info.Text = "Connect first."; return false; }
        if (Session.Protocol != "SSH") { Info.Text = "This tool needs an SSH connection (not verified on " + Session.Protocol + ")."; return false; }
        return true;
    }

    private async void OnScripts(object? s, EventArgs ev0) => await UiSafe.Run(() => OnScripts_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    // Opening Scripts needs no unit: listing, importing and the internet update work offline. Only running a script checks the connection (ScriptsPage).
    private async Task OnScripts_Impl(object? s, EventArgs e) => await Navigation.PushAsync(new ScriptsPage());
    private async void OnReboot(object? s, EventArgs ev0) => await UiSafe.Run(() => OnReboot_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnReboot_Impl(object? s, EventArgs e)
    {
        if (Session.UnitWritesLocked) { Info.Text = Session.LockText; return; }
        if (!RequireSsh()) return;
        if (!Session.WriteMode) { Info.Text = "Turn on Write mode in Settings first."; return; }
        var res = await ConfirmedAction.RunAsync<IMibConnectionService>(() => Session.Svc, () => Session.WriteMode && Session.Protocol == "SSH",
            async () => string.Equals((await DisplayPromptAsync("Reboot unit", "This restarts " + Session.Host + ". Unsaved work on the unit is lost and the connection ends.\nType REBOOT to confirm.", "Reboot", "Cancel"))?.Trim(), "REBOOT", StringComparison.Ordinal),
            RebootRunAsync);
        if (res != GuardResult.Done && res != GuardResult.Declined) Info.Text = ConfirmedAction.Describe(res, "rebooted");
    }

    private async Task RebootRunAsync(IMibConnectionService svc)
    {
        try
        {
            Info.Text = "Synchronizing filesystems...";
            await Task.Run(() => svc.ExecuteCommandAsync("sync"));
            if (!svc.IsConnected) throw new InvalidOperationException("Disconnected before the reboot command was sent.");
            Info.Text = "Sending reboot request...";
            await Task.Run(async () =>
            {
                try { await svc.ExecuteCommandAsync("on -f rcc /usr/apps/mib2_ioc_flash reboot"); }
                catch (Exception ex) when (ex is System.IO.IOException or System.Net.Sockets.SocketException or ObjectDisposedException or Renci.SshNet.Common.SshConnectionException) { } // the link closing is expected
            });
            try { await svc.DisconnectAsync(); } catch { }
            if (Session.ClearIfCurrent(svc)) Session.SetStatus("Disconnected (reboot requested)");
            string host = Session.Host; int port = Session.Port;
            Info.Text = "Reboot requested. Watching " + host + ":" + port + " for it to go down and come back...";
            var ev = await new RebootWatcher().WatchAsync(host, port, m => MainThread.BeginInvokeOnMainThread(() => Info.Text = m));
            switch (ev)
            {
                case RebootEvidence.DownThenUp:
                    await Task.Delay(8000);
                    Info.Text = "The unit stopped answering and is reachable again. That shows an interruption, not proof of a reboot. Reconnecting (Write mode stays off)...";
                    if (!Session.RequestReconnect()) Info.Text = "The unit is reachable again (not proof of a reboot). Open the Connect tab and connect again.";
                    break;
                case RebootEvidence.WentDownOnly: Info.Text = "The unit stopped answering and has not come back yet. Reconnect from the Connect tab when it is up."; break;
                default: Info.Text = "Reboot was requested, but the unit never stopped answering within a minute, so nothing shows it restarted. Do not assume it did."; break;
            }
        }
        catch (Exception ex) { Info.Text = "Reboot failed: " + ex.Message; }
    }

    private async void OnUninstallSsh(object? s, EventArgs ev0) => await UiSafe.Run(() => OnUninstallSsh_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnUninstallSsh_Impl(object? s, EventArgs e)
    {
        if (Session.UnitWritesLocked) { Info.Text = Session.LockText; return; }
        if (!RequireSsh()) return;
        if (!Session.WriteMode) { Info.Text = "Turn on Write mode in Settings first."; return; }
        string effects = string.Join("\n- ", DirectSshUninstall.Effects);
        var res = await ConfirmedAction.RunAsync<IMibConnectionService>(() => Session.Svc, () => Session.WriteMode && Session.Protocol == "SSH",
            async () => string.Equals((await DisplayPromptAsync("Uninstall SSH from " + Session.Host, "This removes the SSH install from the unit without an SD package:\n- " + effects + "\n\nThe startup.sh hook is kept. The current SSH session may stop. After this you cannot reconnect over SSH until you install it again from an SD package.\n\nType UNINSTALL to confirm.", "Uninstall", "Cancel"))?.Trim(), "UNINSTALL", StringComparison.Ordinal),
            UninstallRunAsync);
        if (res != GuardResult.Done && res != GuardResult.Declined) Info.Text = ConfirmedAction.Describe(res, "run");
    }

    private async Task UninstallRunAsync(IMibConnectionService svc)
    {
        try
        {
            Info.Text = "Running the uninstall command...";
            string output = ""; bool dropped = false;
            await Task.Run(async () =>
            {
                try { output = await svc.ExecuteCommandAsync(DirectSshUninstall.Command); }
                catch (Exception ex) when (ex is System.IO.IOException or System.Net.Sockets.SocketException or ObjectDisposedException or Renci.SshNet.Common.SshConnectionException) { dropped = true; }
            });
            var outcome = DirectSshUninstall.Interpret(output, dropped);
            try { await svc.DisconnectAsync(); } catch { }
            if (Session.ClearIfCurrent(svc)) Session.SetStatus("Disconnected (SSH uninstall run)");
            Info.Text = DirectSshUninstall.Describe(outcome, output);
        }
        catch (Exception ex) { Info.Text = "Uninstall failed: " + ex.Message + ". The unit may be partly changed."; }
    }

    private async void OnTransfers(object? s, EventArgs ev0) => await UiSafe.Run(() => OnTransfers_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnTransfers_Impl(object? s, EventArgs e) => await Navigation.PushAsync(new TransfersPage());
    private async void OnSd(object? s, EventArgs ev0) => await UiSafe.Run(() => OnSd_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnSd_Impl(object? s, EventArgs e) => await Navigation.PushAsync(new SdPackagesPage());

    private async void OnSettings(object? s, EventArgs ev0) => await UiSafe.Run(async () => await Navigation.PushAsync(new SettingsPage()), m => DisplayAlert("Error", m, "OK"));
    private async void OnTerminal(object? s, EventArgs ev0) => await UiSafe.Run(OnTerminal_Impl, m => DisplayAlert("Error", m, "OK"));
    private async Task OnTerminal_Impl()
    {
        if (Session.UnitWritesLocked) { Info.Text = Session.LockText; return; }
        if (Session.Svc is null) { Info.Text = "Connect first. The terminal needs a connection to the unit."; return; }
        if (!Session.CanTerminal) { Info.Text = "FTP has no shell. Connect with SSH or Telnet to use the terminal."; return; }
        await Navigation.PushAsync(new TerminalPage(Session.Svc));
    }
}
