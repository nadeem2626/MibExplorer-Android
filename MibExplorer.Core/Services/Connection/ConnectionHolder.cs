namespace MibExplorer.Services;

/// <summary>Holds the one current connection. A late failure of an old operation may clear the connection only if the holder still holds that same connection.</summary>
public sealed class ConnectionHolder<T> where T : class
{
    private T? _current;
    public T? Current { get => Volatile.Read(ref _current); set => Volatile.Write(ref _current, value); }
    /// <summary>Clears only when <paramref name="captured"/> is still the current connection. Returns true if it cleared.</summary>
    public bool ClearIfCurrent(T captured) => ReferenceEquals(Interlocked.CompareExchange(ref _current, null, captured), captured);
}
