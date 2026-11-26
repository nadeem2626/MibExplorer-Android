namespace MibExplorer.Services;

/// <summary>Replaces a local file without ever leaving it truncated: the bytes go to a uniquely named staging file in the same folder (created with CreateNew, so it can never be another operation's file), are flushed, and then moved over the target in one rename. On any failure only that staging file is deleted and the old file stays as it was. The rename replaces the old file; there is no copy of the old file besides the caller's own backup.</summary>
public static class AtomicFile
{
    public static void WriteAllBytes(string path, byte[] bytes, Action? beforeCommit = null)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        string stage = Path.Combine(dir, "." + Path.GetFileName(path) + ".save-" + Guid.NewGuid().ToString("N")[..10]);
        try
        {
            using (var fs = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            beforeCommit?.Invoke();
            File.Move(stage, path, true);
        }
        catch
        {
            try { if (File.Exists(stage)) File.Delete(stage); } catch { }
            throw;
        }
    }
}
