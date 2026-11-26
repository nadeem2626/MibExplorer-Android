using MibExplorer.Services;
namespace MibExplorer.Android;

/// <summary>SecureStorage adapter for the Core key slots, and the shared warning text.</summary>
public sealed class SecureKeyStore : IKeyStore
{
    public static readonly SecureKeyStore Instance = new();
    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);
    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);
    public void Remove(string key) => SecureStorage.Default.Remove(key);
}

public static class KeyRotation
{
    public static async Task<string> DiscardNoteAsync()
    {
        try { return await KeySlots.RotationDiscardsPreviousAsync(SecureKeyStore.Instance) ? "\n\nWARNING: a previous key is already saved. It will be DISCARDED and cannot be recovered (the current key becomes the previous one)." : ""; }
        catch { return ""; }
    }
}
