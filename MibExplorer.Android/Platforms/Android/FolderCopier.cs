using Android.App;
using Android.Content;
using Android.Provider;
using MibExplorer.Services;

namespace MibExplorer.Android;

/// <summary>Lets the user pick a directory (Android system tree picker) and copies it into app cache so the normal folder upload can walk it. Built only; not run on a device.</summary>
public static class FolderCopier
{
    public const int RequestCode = 4711;
    static TaskCompletionSource<global::Android.Net.Uri?>? pending;
    public sealed record Result(string LocalDir, string PickedName, int Files, long Bytes, List<string> Notes);

    public static void OnResult(int requestCode, Result_ resultCode, Intent? data)
    {
        if (requestCode != RequestCode) return;
        pending?.TrySetResult(resultCode == Result_.Ok ? data?.Data : null);
    }
    // Result_ alias so MainActivity can call without a using clash
    public enum Result_ { Ok = -1, Canceled = 0 }

    public static async Task<Result?> PickAndCopyAsync(long maxBytes, CancellationToken ct)
    {
        var act = Platform.CurrentActivity ?? throw new InvalidOperationException("No activity");
        pending = new TaskCompletionSource<global::Android.Net.Uri?>();
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        act.StartActivityForResult(intent, RequestCode);
        var tree = await pending.Task; if (tree is null) return null;
        var resolver = act.ContentResolver!;
        string treeDocId = DocumentsContract.GetTreeDocumentId(tree)!;
        string rootName = treeDocId.Contains(':') ? treeDocId[(treeDocId.LastIndexOf(':') + 1)..] : treeDocId;
        rootName = rootName.Contains('/') ? rootName[(rootName.LastIndexOf('/') + 1)..] : rootName;
        if (string.IsNullOrWhiteSpace(rootName)) rootName = "folder";
        string local = Path.Combine(FileSystem.CacheDirectory, "dir_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(local);
        int files = 0; long bytes = 0; var notes = new List<string>();
        void Walk(string docId, string ldir, int depth)
        {
            if (depth > 12) { notes.Add(ldir + " (too deep, skipped)"); return; }
            var kids = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, docId)!;
            var entries = new List<(string id, string name, string mime, long size)>();
            using (var c = resolver.Query(kids, new[] { DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName, DocumentsContract.Document.ColumnMimeType, DocumentsContract.Document.ColumnSize }, null, null, null))
                while (c != null && c.MoveToNext()) entries.Add((c.GetString(0)!, c.GetString(1) ?? "", c.GetString(2) ?? "", c.IsNull(3) ? 0 : c.GetLong(3)));
            var map = UploadNameMap.Build(entries.Select(e => e.name));
            for (int i = 0; i < entries.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var e = entries[i]; string safe = map[i].Safe;
                if (map[i].Changed) notes.Add(map[i].Original + " -> " + safe);
                string lp = Path.Combine(ldir, safe);
                if (e.mime == DocumentsContract.Document.MimeTypeDir) { Directory.CreateDirectory(lp); Walk(e.id, lp, depth + 1); }
                else
                {
                    if (bytes + e.size > maxBytes) throw new IOException("The folder is larger than the " + (maxBytes >> 20) + " MB limit for one upload.");
                    var uri = DocumentsContract.BuildDocumentUriUsingTree(tree, e.id)!;
                    using var inp = resolver.OpenInputStream(uri); using var o = File.Create(lp);
                    inp!.CopyTo(o); files++; bytes += e.size;
                }
            }
        }
        try { await Task.Run(() => Walk(treeDocId, local, 0), ct); }
        catch { try { Directory.Delete(local, true); } catch { } throw; }
        return new Result(local, rootName, files, bytes, notes);
    }
}
