namespace MibExplorer.Services;

/// <summary>
/// UI-independent state of the Files screen navigation: which folder is displayed, which one is being opened, and which listing results may still apply.
/// Rules: only the newest listing may change state; a result from another connection never changes the displayed path; a failed current listing
/// clears the pending path; Up starts from the pending path when one is being opened, otherwise from the displayed path.
/// </summary>
public sealed class ListNavigator<TConn> where TConn : class
{
    private readonly object _lock = new();
    private int _gen;
    public string? Displayed { get; private set; }
    public string? Pending { get; private set; }
    public TConn? DisplayedConn { get; private set; }

    public readonly record struct Ticket(int Gen, string Path, TConn Conn);

    public Ticket Begin(string path, TConn conn) { lock (_lock) { Pending = path; return new Ticket(++_gen, path, conn); } }

    /// <summary>True when the result may be shown; then Displayed is updated. currentConn is the connection in use now.</summary>
    public bool Complete(Ticket t, TConn? currentConn)
    {
        lock (_lock)
        {
            if (t.Gen != _gen) return false;                       // a newer listing owns the pending path
            if (!ReferenceEquals(t.Conn, currentConn)) { Pending = null; return false; } // another connection took over
            Displayed = t.Path; DisplayedConn = t.Conn; Pending = null; return true;
        }
    }

    /// <summary>True when the failure belongs to the newest listing (then the pending path is cleared and the error may be shown).</summary>
    public bool Fail(Ticket t) { lock (_lock) { if (t.Gen != _gen) return false; Pending = null; return true; } }

    /// <summary>Connection changed or dropped: nothing from the old connection may survive, and results already in flight are dropped.</summary>
    public void Reset() { lock (_lock) { _gen++; Pending = null; Displayed = null; DisplayedConn = null; } }

    /// <summary>The parent to open for Up, or null when already at the top.</summary>
    public string? UpTarget() { lock (_lock) { string from = Pending ?? Displayed ?? "/"; string up = RemotePathNav.Parent(from); return up == from ? null : up; } }
}

/// <summary>Selection bound to one listing of one connection. A new listing or connection drops every mark.</summary>
public sealed class ListingSelection<TConn, TItem> where TConn : class
{
    private TConn? _conn; private string? _path; private readonly HashSet<TItem> _marked = new();
    public void Bind(TConn conn, string path) { _conn = conn; _path = path; _marked.Clear(); }
    public bool Toggle(TItem item) => _marked.Remove(item) ? false : _marked.Add(item);
    public void Clear() => _marked.Clear();
    public int Count => _marked.Count;
    /// <summary>Marked items, or null when the selection does not belong to this connection and folder (caller must refuse).</summary>
    public IReadOnlyList<TItem>? Take(TConn currentConn, string currentPath) =>
        ReferenceEquals(_conn, currentConn) && _path == currentPath ? _marked.ToList() : null;
}
