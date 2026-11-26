namespace MibExplorer.Android;

/// <summary>Writes startup stages and unhandled exceptions to the app's own files dir (read by Settings > Diagnostics).</summary>
public static class Breadcrumb
{
	const string Dir = "/data/data/com.instinct.mibexplorer/files";
	static readonly object L = new();

	public static void Mark(string stage) => Append("startup-stage.txt", $"{DateTime.Now:HH:mm:ss.fff} pid={Environment.ProcessId} {stage}");

	public static void Crash(string where, object? ex) => Append("last-crash.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {where}\n{ex}\n");

	static void Append(string name, string text)
	{
		try { lock (L) { Directory.CreateDirectory(Dir); File.AppendAllText(Path.Combine(Dir, name), text + "\n"); } } catch { }
	}

	public static void Install()
	{
		AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash("AppDomain.UnhandledException", e.ExceptionObject);
		TaskScheduler.UnobservedTaskException += (_, e) => Crash("UnobservedTask", e.Exception);
		global::Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => Crash("AndroidEnvironment", e.Exception);
	}
}
