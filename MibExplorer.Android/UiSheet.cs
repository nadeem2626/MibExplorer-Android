namespace MibExplorer.Android;

/// <summary>Dark, app-owned replacement for the native action sheet (which rendered as a large light-gray system dialog). Same contract as DisplayActionSheet: returns the tapped option, the cancel text, or null when dismissed with Back.</summary>
public static class UiSheet
{
    public static async Task<string?> Sheet(this Page owner, string title, string cancel, string? destruction, params string[] options)
    {
        var tcs = new TaskCompletionSource<string?>();
        var page = new SheetPage(title, cancel, destruction, options, tcs);
        await owner.Navigation.PushModalAsync(page, false);
        string? result = await tcs.Task;
        try { if (owner.Navigation.ModalStack.Contains(page)) await owner.Navigation.PopModalAsync(false); } catch { }
        return result;
    }

    private sealed class SheetPage : ContentPage
    {
        private readonly TaskCompletionSource<string?> _tcs;
        public SheetPage(string title, string cancel, string? destruction, string[] options, TaskCompletionSource<string?> tcs)
        {
            _tcs = tcs; BackgroundColor = Theme.C("Rail2");
            var list = new VerticalStackLayout { Spacing = 6 };
            list.Add(new Label { Text = title, FontAttributes = FontAttributes.Bold, FontSize = 16, TextColor = Theme.C("Text"), Margin = new Thickness(4, 0, 4, 6) });
            Button B(string text, string? value, bool danger = false, bool strong = false) {
                var b = new Button { Text = text, HorizontalOptions = LayoutOptions.Fill, MinimumHeightRequest = 48, CornerRadius = 10, FontSize = 15,
                    Background = strong ? UiGlass.Secondary : UiGlass.Card, TextColor = Theme.C(danger ? "DangerText" : strong ? "OnSecondary" : "Text") };
                b.Clicked += (_, __) => _tcs.TrySetResult(value); return b; }
            foreach (var o in options) list.Add(B(o, o));
            if (destruction is not null) list.Add(B(destruction, destruction, danger: true));
            list.Add(B(cancel, cancel, strong: true));
            Content = new Grid { Padding = 16, Children = { new Border { MaximumWidthRequest = 520, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center,
                BackgroundColor = Theme.C("Bg"), Stroke = Theme.C("Border"), StrokeThickness = 1, Padding = 14, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                Content = new ScrollView { Content = list } } } };
        }
        protected override bool OnBackButtonPressed() { _tcs.TrySetResult(null); return true; }
    }
}
