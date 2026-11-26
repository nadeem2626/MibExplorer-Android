using Android.App;
using Android.Content.PM;
using Android.OS;

namespace MibExplorer.Android;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnActivityResult(int requestCode, Result resultCode, global::Android.Content.Intent? data)
	{
		base.OnActivityResult(requestCode, resultCode, data);
		FolderCopier.OnResult(requestCode, resultCode == Result.Ok ? FolderCopier.Result_.Ok : FolderCopier.Result_.Canceled, data);
	}

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		Breadcrumb.Mark("MainActivity.OnCreate start");
		try { base.OnCreate(savedInstanceState); Breadcrumb.Mark("MainActivity.OnCreate done"); }
		catch (Exception ex) { Breadcrumb.Crash("MainActivity.OnCreate", ex); throw; }
	}
}
