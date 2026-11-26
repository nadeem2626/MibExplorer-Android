using System.Text.Json;
using Android.Webkit;
using Java.Interop;
using MibExplorer.Services;
using MibExplorer.Services.Editing;

namespace MibExplorer.Android;

public partial class TerminalPage : ContentPage
{
    private const string AssetPrefix = "file:///android_asset/terminal/";
    private readonly IMibConnectionService _svc;
    private IRemoteShellSession? _shell;
    private uint _cols = 80, _rows = 24;
    private readonly TerminalLog _log = new();

    /// <summary>Every way text can reach the shell as a paste ends here: the Paste button, the page's paste request, DOM paste events (routed to the host), and any
    /// multi-character input chunk that contains a line break. Line breaks run commands, so they need confirmation; all of them are capped.</summary>
    private async Task SendPasteAsync(string t, string source)
    {
        var target = _shell; // the session this paste was meant for; it must still be the active one after the confirm dialog
        var verdict = PasteGuard.Classify(t, out int lines);
        if (verdict == PasteVerdict.Empty) { Info.Text = "Nothing to paste."; return; }
        if (verdict == PasteVerdict.TooLarge) { Info.Text = "Paste refused (" + source + "): more than " + PasteGuard.MaxChars + " characters."; return; }
        var res = await ConfirmedAction.RunAsync<IRemoteShellSession>(() => _shell, () => true,
            async () => { if (verdict != PasteVerdict.Confirm) return true; if (await DisplayAlert("Paste " + lines + " lines?", "Line breaks run each line as a command in the shell on the unit.\n\n" + (t.Length > 300 ? t[..300] + "..." : t), "Paste", "Cancel")) return true; Info.Text = "Paste cancelled. Nothing was sent."; return false; },
            async sh => { await sh.WriteInputAsync(t.Replace("\r\n", "\n").Replace("\n", "\r")); Info.Text = "Pasted " + t.Length + " characters."; });
        if (res == GuardResult.NotAllowed) Info.Text = "No shell session.";
        else if (res == GuardResult.OwnerChanged) Info.Text = "The shell session changed. Nothing was pasted.";
    }

    private async Task PasteAsync() => await SendPasteAsync(await Clipboard.Default.GetTextAsync() ?? "", "clipboard");

    private bool _ctrl; private Button? _ctrlBtn;
    private void SetCtrl(bool on) { _ctrl = on; if (_ctrlBtn != null) { _ctrlBtn.Background = on ? UiGlass.Primary : UiGlass.Secondary; _ctrlBtn.TextColor = on ? Theme.C("OnPrimary") : Theme.C("OnSecondary"); } Info.Text = on ? "Ctrl armed: type one letter (for example c for Ctrl+C)." : ""; }

    private void BuildKeys()
    {
        void Add(string label, string data, bool danger = false)
        {
            var b = new Button { Text = label, FontSize = 12, Padding = new Thickness(10, 0), HeightRequest = 36, MinimumHeightRequest = 36, CornerRadius = 8, Background = danger ? UiGlass.Danger : UiGlass.Secondary, TextColor = Theme.C("OnSecondary") };
            b.Clicked += async (_, _) => { try { if (_shell != null) await _shell.WriteInputAsync(data); } catch (Exception ex) { Info.Text = "Send failed: " + ex.Message; } };
            Keys.Children.Add(b);
        }
        Add("Esc", "\u001b"); Add("Tab", "\t");
        _ctrlBtn = new Button { Text = "Ctrl", FontSize = 12, Padding = new Thickness(10, 0), HeightRequest = 36, MinimumHeightRequest = 36, CornerRadius = 8, Background = UiGlass.Secondary, TextColor = Theme.C("OnSecondary") };
        _ctrlBtn.Clicked += (_, _) => SetCtrl(!_ctrl); Keys.Children.Add(_ctrlBtn);
        Add("^C", "\u0003", true);
        Add("\u2191", "\u001b[A"); Add("\u2193", "\u001b[B"); Add("\u2190", "\u001b[D"); Add("\u2192", "\u001b[C");
        Add("Home", "\u001b[H"); Add("End", "\u001b[F"); Add("|", "|"); Add("/", "/"); Add("~", "~"); Add("-", "-");
        Button Act(string label, Func<Task> run)
        {
            var b = new Button { Text = label, FontSize = 12, Padding = new Thickness(10, 0), HeightRequest = 36, MinimumHeightRequest = 36, CornerRadius = 8, Background = UiGlass.Secondary, TextColor = Theme.C("OnSecondary") };
            b.Clicked += async (_, _) => { try { await run(); } catch (Exception ex) { Info.Text = label + " failed: " + ex.Message; } };
            Actions.Children.Add(b); return b;
        }
        Act("Copy", async () => { await Web.EvaluateJavaScriptAsync("window.__mibCopySelection()"); });
        Act("Paste", async () => await PasteAsync());
        Act("Clear", async () => { await Web.EvaluateJavaScriptAsync("window.__mibClear()"); Info.Text = "Screen cleared (the session and the saved log are unchanged)."; });
        var save = new Button { Text = "Save log", FontSize = 12, Padding = new Thickness(10, 0), HeightRequest = 36, MinimumHeightRequest = 36, CornerRadius = 8, Background = UiGlass.Primary, TextColor = Theme.C("OnPrimary") };
        save.Clicked += async (_, _) =>
        {
            try
            {
                string f = Path.Combine(FileSystem.CacheDirectory, "terminal-" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                string txt; lock (_log) txt = _log.ToString(); File.WriteAllText(f, txt);
                await Share.Default.RequestAsync(new ShareFileRequest { Title = "Terminal log", File = new ShareFile(f) });
            }
            catch (Exception ex) { Info.Text = "Save log failed: " + ex.Message; }
        };
        Keys.Children.Add(save);
    }

    /// <summary>The only object exposed to page JS. Only 4 message types are accepted, size-limited, and the page can never leave the local asset folder.</summary>
    private sealed class Bridge : Java.Lang.Object
    {
        private readonly TerminalPage _page;
        public Bridge(TerminalPage page) { _page = page; }
        [JavascriptInterface, Export("postMessage")]
        public void PostMessage(string json) => _page.Dispatch(json);
    }

    private readonly string? _startDir;
    public TerminalPage(IMibConnectionService svc, string? startDir = null)
    {
        _startDir = startDir;
        InitializeComponent();
        _svc = svc;
        BuildKeys();
        CrumbLabel.Text = "Terminal \u203A unit \u203A " + string.Join(" \u203A ", FileListView.Breadcrumb(_startDir ?? "/").Select(c => c.Label));
        Web.HandlerChanged += (_, _) =>
        {
            if (Web.Handler?.PlatformView is not global::Android.Webkit.WebView wv) return;
            wv.Settings.JavaScriptEnabled = true;
            wv.Settings.AllowFileAccess = true;                 // needed for android_asset
            wv.Settings.AllowFileAccessFromFileURLs = false;
            wv.Settings.AllowUniversalAccessFromFileURLs = false;
            wv.Settings.AllowContentAccess = false;
            wv.Settings.SetSupportMultipleWindows(false);
            wv.Settings.BlockNetworkLoads = true;               // the terminal page has no network content; nothing remote may load into a frame that can reach mibHost
            wv.AddJavascriptInterface(new Bridge(this), "mibHost");
            Web.Source = new UrlWebViewSource { Url = AssetPrefix + "index.html" };
        };
    }

    private void OnNavigating(object? s, WebNavigatingEventArgs e)
    {
        if (!e.Url.StartsWith(AssetPrefix, StringComparison.Ordinal)) e.Cancel = true; // nothing but local terminal assets
    }

    private void OnNavigated(object? s, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success) Info.Text = "Terminal page failed to load: " + e.Result;
    }

    private async void Dispatch(string json)
    {
        try
        {
            if (json.Length > 65536) return;
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            switch (r.GetProperty("type").GetString())
            {
                case "ready": _ready = true; await StartShellAsync(); break;
                case "input":
                {
                    string d = r.GetProperty("data").GetString() ?? "";
                    if (PasteGuard.IsPasteChunk(d)) await MainThread.InvokeOnMainThreadAsync(() => SendPasteAsync(d, "typed chunk")); // multi-character chunk with a line break = a paste
                    else if (_ctrl && d.Length == 1) { string? cc = CtrlModifier.Apply(d); await MainThread.InvokeOnMainThreadAsync(() => SetCtrl(false)); if (_shell != null) await _shell.WriteInputAsync(cc ?? d); }
                    else if (_shell != null) await _shell.WriteInputAsync(d);
                    break;
                }
                case "binary": if (_shell != null) await _shell.WriteBytesAsync(Convert.FromBase64String(r.GetProperty("data").GetString() ?? "")); break;
                case "resize":
                    _cols = Math.Clamp(r.GetProperty("cols").GetUInt32(), 10u, 500u); _rows = Math.Clamp(r.GetProperty("rows").GetUInt32(), 3u, 200u);
                    if (_shell != null) await _shell.ResizeAsync(_cols, _rows);
                    break;
                case "clipboardCopy":
                {
                    string t = r.GetProperty("data").GetString() ?? "";
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        if (t.Length == 0) { Info.Text = "Nothing selected. Long-press in the terminal to select text first."; return; }
                        if (t.Length > 1_000_000) { Info.Text = "Selection too large to copy."; return; }
                        await Clipboard.Default.SetTextAsync(t); Info.Text = "Copied " + t.Length + " characters.";
                    });
                    break;
                }
                case "clipboardPaste": await MainThread.InvokeOnMainThreadAsync(PasteAsync); break;
                default: break; // anything unknown is ignored
            }
        }
        catch (Exception ex) { MainThread.BeginInvokeOnMainThread(() => Info.Text = "Terminal error: " + ex.Message); }
    }

    private bool _starting, _ready;
    private async Task StartShellAsync()
    {
        if (_shell != null || _starting) return; _starting = true;
        UiWatchdog.Note("terminal: shell start requested");
        try { _shell = await Task.Run(() => _svc.CreateShellSessionAsync()); MainThread.BeginInvokeOnMainThread(() => SetCtrl(false)); } finally { _starting = false; }
        _shell.TextReceived += async (_, t) =>
        {
            lock (_log) _log.Append(t);
            await MainThread.InvokeOnMainThreadAsync(() =>
                Web.EvaluateJavaScriptAsync("window.__mibRecv(" + JsonSerializer.Serialize(new { type = "output", data = t }) + ")"));
        };
        var mine = _shell;
        _shell.Closed += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
        {
            // Only react if this is still the active shell (an old shell closing must not null a newer one).
            if (!ReferenceEquals(_shell, mine)) return;
            SetCtrl(false); _shell = null; try { mine.Dispose(); } catch { }
            Info.Text = "The shell ended or the connection dropped. Leave this screen and come back, or open the Terminal again, for a new shell.";
        });
        var started = _shell; var cols = _cols; var rows = _rows;
        await Task.Run(async () => // SSH.NET opens the channel synchronously inside StartAsync: never on the UI thread
        {
            await started.StartAsync();
            await started.ResizeAsync(cols, rows);
            if (!string.IsNullOrEmpty(_startDir) && !_startDir.Any(char.IsControl)) await started.SendCommandAsync("cd '" + _startDir.Replace("'", "'\\''") + "'");
        });
        UiWatchdog.Note("terminal: shell started");
    }

    // Leaving the screen ends the shell. Coming back starts a NEW shell on the same connection (the on-screen scrollback stays, the old remote session is gone).
    // The dispose closes the SSH channel, which can wait on the network: it runs off the UI thread so a slow close cannot freeze the app.
    protected override void OnDisappearing() { base.OnDisappearing(); UiWatchdog.Note("terminal: leaving"); SetCtrl(false); var sh = _shell; _shell = null; if (sh != null) _ = Task.Run(() => { try { sh.Dispose(); } catch { } }); }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); UiWatchdog.Note("terminal: appearing");
        if (_ready && _shell == null && !_starting)
        {
            try { Info.Text = "New shell session started."; await Web.EvaluateJavaScriptAsync("window.__mibRecv(" + JsonSerializer.Serialize(new { type = "output", data = "\r\n[new shell session]\r\n" }) + ")"); await StartShellAsync(); }
            catch (Exception ex) { Info.Text = "Could not start a new shell: " + ex.Message; }
        }
    }
}
