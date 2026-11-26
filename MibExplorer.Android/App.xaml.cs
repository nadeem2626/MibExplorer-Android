using Microsoft.Extensions.DependencyInjection;

namespace MibExplorer.Android;

public partial class App : Application
{
	public App()
	{
		Breadcrumb.Mark("App ctor start");
		InitializeComponent();
		Theme.Apply();
		RequestedThemeChanged += (_, __) => { if (Theme.Mode == "system") MainThread.BeginInvokeOnMainThread(() => Theme.Apply()); };
		Breadcrumb.Mark("App InitializeComponent done");
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		Breadcrumb.Mark("CreateWindow"); UiWatchdog.Start();
		try { var w = new Window(new AppShell()); Breadcrumb.Mark("AppShell created"); return w; }
		catch (Exception ex) { Breadcrumb.Crash("CreateWindow/AppShell", ex); throw; }
	}
}