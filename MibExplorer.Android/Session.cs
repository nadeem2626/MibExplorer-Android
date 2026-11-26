using MibExplorer.Services;

namespace MibExplorer.Android;

/// <summary>One shared connection (SSH, Telnet or FTP) for all screens.</summary>
public static class Session
{
    private static readonly ConnectionHolder<IMibConnectionService> _holder = new();
    public static IMibConnectionService? Svc { get => _holder.Current; set { _holder.Current = value; if (value is null) WriteMode = false; } }
    /// <summary>Disconnect only if this exact connection is still the current one (a late failure of an old operation must not drop a newer session).</summary>
    public static bool ClearIfCurrent(IMibConnectionService captured) { bool c = _holder.ClearIfCurrent(captured); if (c) WriteMode = false; return c; }
    public static string Protocol { get; set; } = "";
    public static string Host { get; set; } = "";
    public static int Port { get; set; } = 22;
    public static event Func<Task>? ReconnectRequested;
    public static bool RequestReconnect() { var h = ReconnectRequested; if (h is null) return false; _ = h(); return true; }
    public static string Status { get; private set; } = "Not connected";
    public static string? Path { get; set; }
    public static event Action? StatusChanged;
    public static void SetStatus(string s) { Status = s; StatusChanged?.Invoke(); }
    public static bool IsConnected => Svc is not null;

    // Write mode is per session: always off after (re)connecting. Every write action checks it.
    private static bool _write;
    /// <summary>Kill switch for unit-side writes. Currently false (writes allowed behind Write mode and the per-operation gates).</summary>
    public const bool UnitWritesLocked = false;
    public const string LockText = "This feature is switched off.";
    public static bool WriteMode { get => _write; set { if (value && UnitWritesLocked) return; if (_write == value) return; _write = value; WriteModeChanged?.Invoke(); } }
    public static event Action? WriteModeChanged;
    public static IRemotePermissionService? Perm => Svc as IRemotePermissionService;
    public static bool CanTerminal => Svc is not null && Protocol != "FTP";
}
