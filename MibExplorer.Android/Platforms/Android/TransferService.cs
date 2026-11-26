using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace MibExplorer.Android;

/// <summary>Foreground service that only exists to keep the process alive while the transfer queue has work, so a transfer is not killed when the app goes to the background. It does not make the connection itself survive a Wi-Fi drop. Built only; not run on a device.</summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public class TransferService : Service
{
    const string Channel = "mibx_transfers"; const int NotifId = 4712;
    public static bool Running;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var nm = (NotificationManager)GetSystemService(NotificationService)!;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O) nm.CreateNotificationChannel(new NotificationChannel(Channel, "Transfers", NotificationImportance.Low));
        var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop), PendingIntentFlags.Immutable);
        var n = new Notification.Builder(this, Channel).SetContentTitle("MibExplorer transfer running").SetContentText("Keeping the app alive until the transfer queue is empty.").SetSmallIcon(global::Android.Resource.Drawable.StatSysUpload).SetOngoing(true).SetContentIntent(open).Build();
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q) StartForeground(NotifId, n, ForegroundService.TypeDataSync); else StartForeground(NotifId, n);
        Running = true;
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy() { Running = false; base.OnDestroy(); }

    public static void Sync(bool busy)
    {
        try
        {
            var ctx = global::Android.App.Application.Context;
            if (busy && !Running) { ctx.StartForegroundService(new Intent(ctx, typeof(TransferService))); Running = true; AskNotificationPermission(); }
            else if (!busy && Running) { ctx.StopService(new Intent(ctx, typeof(TransferService))); Running = false; }
        }
        catch (Exception ex) { Breadcrumb.Mark("TransferService.Sync failed: " + ex.GetType().Name + " " + ex.Message); }
    }

    static void AskNotificationPermission()
    {
        try
        {
            var act = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (act != null && Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu && act.CheckSelfPermission("android.permission.POST_NOTIFICATIONS") != Permission.Granted)
                act.RequestPermissions(new[] { "android.permission.POST_NOTIFICATIONS" }, 4713);
        }
        catch { }
    }
}
