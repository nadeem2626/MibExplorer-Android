namespace MibExplorer.Android;

/// <summary>Diagnostic only: every second a pool thread asks the UI thread to answer; if the answer takes over 1.5 s the stall is written to the Diagnostics log (Settings > Diagnostics) with its length and the last screen event.</summary>
public static class UiWatchdog
{
    private static int _started; public static volatile string Last = "";
    public static void Note(string what) { Last = what; Breadcrumb.Mark("event: " + what); }
    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(1000);
                var t0 = DateTime.UtcNow; var tcs = new TaskCompletionSource();
                try { MainThread.BeginInvokeOnMainThread(() => tcs.TrySetResult()); } catch { continue; }
                var done = await Task.WhenAny(tcs.Task, Task.Delay(1500));
                if (done != tcs.Task) { await tcs.Task; Breadcrumb.Mark($"UI THREAD STALL {(DateTime.UtcNow - t0).TotalMilliseconds:0} ms (last event: {Last})"); }
            }
        });
    }
}
