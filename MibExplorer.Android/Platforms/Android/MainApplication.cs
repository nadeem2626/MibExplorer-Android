using Android.App;
using Android.Runtime;

namespace MibExplorer.Android;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
		Breadcrumb.Install();
		Breadcrumb.Mark("MainApplication ctor");
	}

	protected override MauiApp CreateMauiApp()
	{
		Breadcrumb.Mark("CreateMauiApp start");
		try { var a = MauiProgram.CreateMauiApp(); Breadcrumb.Mark("CreateMauiApp done"); return a; }
		catch (Exception ex) { Breadcrumb.Crash("CreateMauiApp", ex); throw; }
	}
}
