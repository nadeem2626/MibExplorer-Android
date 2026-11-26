using MibExplorer.Models;
using MibExplorer.Services;

namespace MibExplorer.Android;

/// <summary>Read-only details and text preview of one remote item. Bound to the connection that listed it.</summary>
public partial class DetailsPage : ContentPage
{
    private readonly IMibConnectionService _svc; private readonly RemoteExplorerItem _it;

    public DetailsPage(IMibConnectionService svc, RemoteExplorerItem item)
    {
        InitializeComponent(); _svc = svc; _it = item;
        TitleLabel.Text = "unit \u203A " + string.Join(" \u203A ", FileListView.Breadcrumb(item.FullPath).Select(c => c.Label));
        foreach (var (label, value) in FileDetails.Rows(item, Session.WriteMode))
            Kv.Add(new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 12, Children = {
                new Label { Text = label, TextColor = Theme.C("Muted"), VerticalOptions = LayoutOptions.Start },
                new Label { Text = value, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.End, HorizontalOptions = LayoutOptions.End }.Col(1) } });
        bool file = item.EntryType == RemoteEntryType.File;
        DownloadBtn.IsVisible = file;
        EditBtn.IsVisible = file;
        TermBtn.IsVisible = Session.CanTerminal && !Session.UnitWritesLocked;
        SizeChanged += (_, __) => LayoutCards();
        _ = LoadPreviewAsync();
    }

    // Two cards side by side when the page is wide (as in the design), stacked otherwise.
    private void LayoutCards()
    {
        bool wide = Width >= 640;
        Grid.SetRow(PreviewCard, wide ? 0 : 1); Grid.SetColumn(PreviewCard, wide ? 1 : 0);
        Grid.SetColumnSpan(DetailsCard, wide ? 1 : 2); Grid.SetColumnSpan(PreviewCard, wide ? 1 : 2);
    }

    private async Task LoadPreviewAsync()
    {
        var kind = FileDetails.CanPreview(_it);
        switch (kind)
        {
            case FileDetails.PreviewKind.NotAFile: PreviewCard.IsVisible = false; return;
            case FileDetails.PreviewKind.Empty: PreviewText.Text = "(empty file)"; return;
            case FileDetails.PreviewKind.TooLarge: PreviewText.Text = $"Too large for a preview (over {FileDetails.MaxPreviewBytes / 1024} KB). Use Download or Edit."; return;
        }
        try
        {
            string raw = await _svc.ReadRemoteTextFileAsync(_it.FullPath);
            if (!ReferenceEquals(Session.Svc, _svc)) { PreviewText.Text = "The connection changed. Preview not shown."; return; }
            var p = FileDetails.Make(raw);
            PreviewText.Text = p.Kind switch { FileDetails.PreviewKind.Binary => "Binary file. No preview.", FileDetails.PreviewKind.Empty => "(empty file)", _ => p.Text };
        }
        catch (Exception ex) { PreviewText.Text = "Preview failed: " + ex.Message; }
    }

    private bool Current() { if (ReferenceEquals(Session.Svc, _svc)) return true; Note.Text = "The connection changed since this item was listed. Go back and refresh."; return false; }

    private async void OnDownload(object? s, EventArgs e)
    {
        if (!Current()) return;
        try
        {
            // Owned staging folder per download: a failed or cancelled run removes only its own folder, never an earlier copy or another download's file.
            string own = System.IO.Path.Combine(FileSystem.AppDataDirectory, "downloads", Guid.NewGuid().ToString("N")[..8]);
            var local = System.IO.Path.Combine(own, System.IO.Path.GetFileName(_it.FullPath));
            Directory.CreateDirectory(own);
            Note.Text = "Downloading " + _it.Name + "...";
            var job = await Transfers.Queue.RunAsync("Download", _it.FullPath, async (ct, pr) => { await _svc.DownloadFileAsync(_it.FullPath, local, pr, ct); return new FileInfo(local).Length + " bytes"; });
            if (job.State != TransferState.Done) { try { Directory.Delete(own, true); } catch { } Note.Text = "Download " + job.State.ToString().ToLower() + ": " + job.Detail; return; }
            Note.Text = "Downloaded: " + _it.Name;
            await Share.Default.RequestAsync(new ShareFileRequest { Title = "Save " + _it.Name, File = new ShareFile(local) });
        }
        catch (Exception ex) { Note.Text = "Download error: " + ex.Message; }
    }

    private async void OnEdit(object? s, EventArgs ev0) => await UiSafe.Run(() => OnEdit_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnEdit_Impl(object? s, EventArgs e) { if (Current()) await Navigation.PushAsync(new EditorPage(_it.FullPath)); }
    private async void OnTerminal(object? s, EventArgs ev0) => await UiSafe.Run(() => OnTerminal_Impl(s, ev0), m => DisplayAlert("Error", m, "OK"));
    private async Task OnTerminal_Impl(object? s, EventArgs e)
    {
        if (!Current()) return;
        await Navigation.PushAsync(new TerminalPage(_svc, WriteGuard.Parent(WriteGuard.Normalize(_it.FullPath)))); // opens in the folder that holds the item
    }
}

static class GridExt { public static T Col<T>(this T v, int c) where T : BindableObject { Grid.SetColumn(v, c); return v; } }
