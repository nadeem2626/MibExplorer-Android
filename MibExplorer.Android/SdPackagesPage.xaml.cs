using System.Security.Cryptography;
using MibExplorer.Services;

namespace MibExplorer.Android;

public partial class SdPackagesPage : ContentPage
{
    private static string Root => Path.Combine(FileSystem.AppDataDirectory, "sd-packages");
    public SdPackagesPage() { InitializeComponent(); Directory.CreateDirectory(Root); SdUpdatePackageBuilder.OutputRoot = Root; SdUninstallPackageBuilder.OutputRoot = Root; ShowKey(); }

    private void ShowKey()
    {
        KeyInfo.Text = "Uses the same SSH key as the Connect screen (created now if missing). The private key stays on this device.";
    }

    private async void OnBuildInstall(object? s, EventArgs ev0) => await UiSafe.Run(() => OnBuildInstall_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnBuildInstall_Impl(object? s, EventArgs e)
    {
        if (_building) { Status.Text = "A package is already being built."; return; } // must precede touching the shared key override
        // Same key the Connect screen uses (Android Keystore), so a connection after install works with it.
        string? pem = await SecureStorage.Default.GetAsync("ssh_key_pem");
        if (!string.IsNullOrEmpty(await SecureStorage.Default.GetAsync("ssh_key_pass"))) { KeyInfo.Text = "The saved key has a passphrase (imported). The installer needs a key without one: generate a new key on the Connect tab first."; return; }
        if (string.IsNullOrEmpty(pem))
        {
            var (p, pub) = SshKeyService.GenerateInMemory("mibexplorer-android");
            await SecureStorage.Default.SetAsync("ssh_key_pem", p); await SecureStorage.Default.SetAsync("ssh_key_pub", pub); pem = p;
        }
        SdUpdatePackageBuilder.PrivateKeyPemOverride = pem;
        try { await Build(() => new SdUpdatePackageBuilder().BuildPackage()); } finally { SdUpdatePackageBuilder.PrivateKeyPemOverride = null; }
        ShowKey();
    }
    private async void OnBuildKeyUpdate(object? s, EventArgs ev0) => await UiSafe.Run(() => OnBuildKeyUpdate_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnBuildKeyUpdate_Impl(object? s, EventArgs e)
    {
        if (_building) { Status.Text = "A package is already being built."; return; }
        if (!await DisplayAlert("New key?", "A new SSH key is created and saved on this device. The current key is kept as 'previous'. The unit keeps accepting only the old key until you install the built package from SD. Continue?" + await KeyRotation.DiscardNoteAsync(), "Create", "Cancel")) return;
        try
        {
            var (p, pub) = SshKeyService.GenerateInMemory("mibexplorer-android");
            await KeySlots.RotateAsync(SecureKeyStore.Instance, p, pub);
            SdUpdatePackageBuilder.PrivateKeyPemOverride = p;
            bool built;
            try { built = await Build(() => new SdUpdatePackageBuilder().BuildPackage()); } finally { SdUpdatePackageBuilder.PrivateKeyPemOverride = null; }
            if (!built) Status.Text += " The NEW key is already the active key, but no package for the unit exists. The unit still accepts only the previous key: use 'Switch back to previous key' to connect again.";
        }
        catch (Exception ex) { Status.Text = "Key update failed: " + ex.Message; }
    }

    private async void OnPrevKey(object? s, EventArgs ev0) => await UiSafe.Run(() => OnPrevKey_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnPrevKey_Impl(object? s, EventArgs e)
    {
        if (!await KeySlots.HasPreviousAsync(SecureKeyStore.Instance)) { Status.Text = "No previous key saved."; return; }
        if (!await DisplayAlert("Switch back to the previous key?", "The previous key becomes the active key. The key that is active now is kept as the new 'previous' key, so you can switch again. Nothing is deleted.", "Switch", "Cancel")) return;
        if (!await KeySlots.SwapWithPreviousAsync(SecureKeyStore.Instance)) { Status.Text = "No previous key saved."; return; }
        Status.Text = "The previous key is active again. The key that was active is kept as 'previous'.";
    }

    private async void OnBuildUninstall(object? s, EventArgs ev0) => await UiSafe.Run(() => OnBuildUninstall_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnBuildUninstall_Impl(object? s, EventArgs e) => await Build(() => new SdUninstallPackageBuilder().BuildPackage());

    private bool _building;
    private async Task<bool> Build(Func<string> make)
    {
        if (_building) { Status.Text = "A package is already being built."; return false; }
        _building = true;
        try
        {
            Status.Text = "Building...";
            string zip = await Task.Run(make);
            ShowKey();
            Status.Text = $"Built {Path.GetFileName(zip)} ({new FileInfo(zip).Length} bytes). Not tested on a unit.";
            await Share.Default.RequestAsync(new ShareFileRequest { Title = "Save " + Path.GetFileName(zip), File = new ShareFile(zip) });
            return true;
        }
        catch (Exception ex) { Status.Text = "Build failed: " + ex.Message; return false; }
        finally { _building = false; }
    }
}
