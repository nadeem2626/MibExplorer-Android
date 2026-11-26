using MibExplorer.Services;
namespace MibExplorer.Android;

public partial class AppShell : Shell
{
	private NavMode? _mode;
	/// <summary>True while the landscape rail is shown (Settings is a rail item then, and a Tools tile otherwise).</summary>
	public static bool RailVisible => Current is AppShell a && a._mode == NavMode.SideRail;
	private readonly Dictionary<string, (Border box, Label label)> _railItems = new();
	private readonly Dictionary<string, (Image img, string png)> _railIcons = new();
	private Image? _railLogo; private ScrollView? _railScroll;
	// The rail is black with the Night look in BOTH modes (user decision): fixed colours, never the Day palette.
	private static readonly Color RailBg = Color.FromArgb("#080F14"), RailMuted = Color.FromArgb("#9FB0BC"), RailSel = Color.FromArgb("#17262F"), RailAccent = Color.FromArgb("#7FD3E0");
	private const string RailLogoFile = "logo_mib.png";
	private string _active = "Connect";
	private string _root = "Connect";   // route of the current tab; Tools children such as Terminal and Scripts stay under it
	private string? _pushed;            // Transfers or Settings while that page is open over the tab
	public static void PageShown(string route) { if (Current is AppShell a) { a._pushed = route; a.Highlight(); } }
	public static void PageHidden(string route) { if (Current is AppShell a && a._pushed == route) { a._pushed = null; a.Highlight(); } }
	// The highlight follows the real navigation stack, not only the remembered flag: a missed Appearing/Disappearing event can no longer leave Settings painted after another tab is shown.
	// Transfers has no rail entry: Tools stays highlighted while it is open.
	private void Highlight()
	{
		bool settingsOpen = false;
		try { foreach (var p in Navigation.NavigationStack) if (p is SettingsPage || p is AboutPage || p is LicensesPage) { settingsOpen = true; break; } } catch { settingsOpen = _pushed == "Settings"; }
		_active = settingsOpen && _railItems.ContainsKey("Settings") ? "Settings" : _root;
		PaintRail();
	}

	public AppShell()
	{
		InitializeComponent();
		FlyoutContent = BuildRail();
		Loaded += (_, __) => { Breadcrumb.Mark($"layout: Shell loaded {Width:0}x{Height:0}"); Reapply("loaded"); if (Window is not null) Window.SizeChanged += (_, ___) => Reapply("window"); };
		Navigated += (_, __) => { var r = NavLayout.RouteOf(CurrentState?.Location?.OriginalString); if (r != null) { _root = r; Highlight(); } };
	}

	// Layout follows the window size (landscape from 600 wide gets the side rail of the design; otherwise the bottom tabs). Pages are not recreated, so screen state survives rotation.
	protected override void OnSizeAllocated(double width, double height)
	{
		base.OnSizeAllocated(width, height);
		Apply(width, height, "allocated");
	}

	// Second and third triggers: the shell's own allocation is not the only signal that the window changed.
	// The locked rail is not inset by the framework like the page content is: start it below the status bar (clock) so it lines up with the page.
	private void ApplyRailTopInset()
	{
#if ANDROID
		try
		{
			var act = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity; var decor = act?.Window?.DecorView; if (act is null || decor is null || _railScroll is null) return;
			int px = 0;
			var ins = decor.RootWindowInsets;
			if (ins is not null) px = global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.R ? ins.GetInsets(global::Android.Views.WindowInsets.Type.StatusBars()).Top : ins.SystemWindowInsetTop;
			double dp = px / (double)act.Resources!.DisplayMetrics!.Density;
			if (Math.Abs(_railScroll.Margin.Top - dp) > 0.5) { _railScroll.Margin = new Thickness(0, dp, 0, 0); Breadcrumb.Mark($"layout: rail top inset {dp:0.#} dp"); }
		}
		catch (Exception ex) { System.Diagnostics.Debug.WriteLine("rail inset: " + ex.Message); }
#endif
	}

	private void Reapply(string why) { ApplyRailTopInset(); var w = Window; if (w is not null && w.Width > 0 && w.Height > 0) Apply(w.Width, w.Height, why); else Apply(Width, Height, why); }

	private void Apply(double width, double height, string why)
	{
		ApplyRailTopInset();
		var mode = NavLayout.Choose(width, height);
		UiWatchdog.Last = "layout"; Breadcrumb.Mark($"layout[{why}]: {width:0}x{height:0} -> {mode}{(mode == _mode ? " (unchanged)" : "")}");
		if (mode == _mode) return;
		_mode = mode;
		bool rail = mode == NavMode.SideRail;
		FlyoutBehavior = rail ? FlyoutBehavior.Locked : FlyoutBehavior.Disabled;
		// Verified in the MAUI source (ShellItem.ShowTabs / Shell.GetEffectiveValue): the value is read from the current page, then ShellContent, section and item, and the walk stops before the Shell itself.
		// So it must be set on every ShellContent; a value on the Shell alone is never read.
		foreach (var item in Items) foreach (var section in item.Items) foreach (var content in section.Items) Shell.SetTabBarIsVisible(content, !rail);
		var r = NavLayout.RouteOf(CurrentState?.Location?.OriginalString); if (r != null) _root = r;
		Highlight();
		Breadcrumb.Mark($"layout applied: flyout={FlyoutBehavior} tabs={(rail ? "hidden" : "visible")}");
	}

	private View BuildRail()
	{
		FlyoutWidth = 132; FlyoutBackgroundColor = Colors.Black;
		var stack = new VerticalStackLayout { Spacing = 6, Padding = new Thickness(8, 14) };
		_railLogo = new Image { Source = RailLogoFile, WidthRequest = 72, HeightRequest = 72, HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 0, 6) }; stack.Add(_railLogo); // the project MIB logo (same art as the app icon)
		foreach (var it in NavLayout.Items)
		{
			var label = new Label { Text = it.Title, FontSize = 13, HorizontalOptions = LayoutOptions.Center, TextColor = RailMuted };
			string png = it.Route == "Tools" ? "rail_tools.png" : it.Icon; /* wrench only in the landscape rail; the portrait tab keeps its original icon */
			var img = new Image { Source = png, WidthRequest = 24, HeightRequest = 24, HorizontalOptions = LayoutOptions.Center, Opacity = 0.8 }; _railIcons[it.Route] = (img, png);
			var content = new VerticalStackLayout { Spacing = 4, Children = { img, label } };
			var box = new Border { StrokeThickness = 0, Padding = new Thickness(4, 10), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 }, Content = content, BackgroundColor = Colors.Transparent };
			var tap = new TapGestureRecognizer(); var item = it;
			tap.Tapped += async (_, __) =>
			{
				try
				{
					if (item.IsPushedPage)
					{
						if (item.Route == "Settings" && Shell.Current.CurrentPage is not SettingsPage) await Shell.Current.Navigation.PushAsync(new SettingsPage());
					}
					else if (Shell.Current is not null) { await Shell.Current.GoToAsync("//" + item.Route); var r = NavLayout.RouteOf(CurrentState?.Location?.OriginalString); if (r != null) _root = r; Highlight(); }
				}
				catch (Exception ex) { System.Diagnostics.Debug.WriteLine("rail nav: " + ex.Message); }
			};
			box.GestureRecognizers.Add(tap); stack.Add(box); _railItems[it.Route] = (box, label);
		}
		_railScroll = new ScrollView { BackgroundColor = RailBg, Content = stack }; return _railScroll;
	}

	/// <summary>Re-colours the rail in place after a theme switch (colours, logo and icon files). Navigation state is not touched.</summary>
	public void ApplyTheme()
	{
		// The rail does not change with the mode; only the highlight is repainted.
		PaintRail();
	}

	private void PaintRail()
	{
		foreach (var kv in _railItems)
		{
			bool on = kv.Key == _active;
			kv.Value.box.BackgroundColor = on ? RailSel : Colors.Transparent;
			kv.Value.label.TextColor = on ? RailAccent : RailMuted;
		}
	}
}
