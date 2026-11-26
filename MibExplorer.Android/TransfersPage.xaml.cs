using MibExplorer.Services;
namespace MibExplorer.Android;

public partial class TransfersPage : ContentPage
{
    protected override void OnAppearing() { base.OnAppearing(); AppShell.PageShown("Transfers"); }
    protected override void OnDisappearing() { base.OnDisappearing(); AppShell.PageHidden("Transfers"); }
    public TransfersPage() { InitializeComponent(); Transfers.Queue.Changed += () => MainThread.BeginInvokeOnMainThread(Fill); Fill(); }

    // Cards as in the design: name, status line, progress bar (full when done, empty while waiting), and one "Cancel active transfer" button.
    private void Fill()
    {
        Body.Children.Clear();
        List<TransferJob> copy; lock (Transfers.Queue.Items) copy = Transfers.Queue.Items.ToList();
        if (copy.Count == 0) Body.Children.Add(new Label { Text = "No transfers in this session yet.", TextColor = Theme.C("Muted") });
        foreach (var t in copy)
        {
            string status = t.State switch
            {
                TransferState.Queued => "Waiting",
                TransferState.Running => t.Percent is double pc ? Math.Round(pc) + " %" : (t.BytesDone > 0 ? t.BytesDone + " bytes so far" : "Working (size unknown)"),
                TransferState.Done => t.Detail.Length > 0 ? "Done. " + t.Detail : "Done",
                TransferState.Failed => "Failed: " + t.Detail,
                _ => t.Detail.Length > 0 ? t.Detail : "Cancelled"
            };
            double? prog = t.State == TransferState.Done ? 1 : t.State == TransferState.Running ? (t.Percent is double p ? p / 100d : null) : 0;
            var card = new Border { Background = UiGlass.Card, Stroke = Theme.C("GlassStroke"), StrokeThickness = 1, Padding = 12, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 } };
            var col = new VerticalStackLayout { Spacing = 4 };
            col.Children.Add(new Label { Text = t.Kind + "  " + t.Name, FontAttributes = FontAttributes.Bold, TextColor = Theme.C("Text"), LineBreakMode = LineBreakMode.MiddleTruncation });
            col.Children.Add(new Label { Text = status, FontSize = 13, TextColor = Theme.C(t.State == TransferState.Failed ? "FailText" : "Muted") });
            col.Children.Add(prog is double pr ? new ProgressBar { Progress = pr, ProgressColor = Theme.C("Accent"), HeightRequest = 6 } : new ProgressBar { IsVisible = true, Progress = 0, HeightRequest = 6 });
            if (t.State == TransferState.Queued) { var qb = new Button { Text = "Cancel", HeightRequest = 34, Padding = new Thickness(10, 0), Background = UiGlass.Danger, TextColor = Theme.C("OnSecondary"), HorizontalOptions = LayoutOptions.Start }; var jj = t; qb.Clicked += (_, _) => Transfers.Queue.Cancel(jj); col.Children.Add(qb); }
            card.Content = col; Body.Children.Add(card);
        }
        var running = copy.FirstOrDefault(x => x.State == TransferState.Running);
        if (running != null)
        {
            var cb = new Button { Text = "Cancel active transfer", Background = UiGlass.Secondary, TextColor = Theme.C("OnSecondary"), FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Start, Padding = new Thickness(16, 0) };
            cb.Clicked += (_, _) => Transfers.Queue.Cancel(running); Body.Children.Add(cb);
        }
    }

    private void OnClear(object? s, EventArgs e) { Transfers.Queue.ClearFinished(); }
}
