namespace MibExplorer.Android;

public partial class AboutPage : ContentPage, IThemeAware
{
    public AboutPage()
    {
        InitializeComponent();
        VersionLbl.Text = "Version " + AppInfo.Current.VersionString;
        LogoImg.Source = Theme.Logo;
    }

    public void OnThemeChanged() => LogoImg.Source = Theme.Logo;

    private async void OnLicenses(object? s, EventArgs ev0) => await UiSafe.Run(() => Navigation.PushAsync(new LicensesPage()), m => DisplayAlert("Error", m, "OK"));
}
