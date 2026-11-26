using MibExplorer.Models; using MibExplorer.Services;
public static class FileDetailsTests
{
    public static int Run()
    {
        int fail = 0; void Check(string n, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + n); if (!ok) fail++; }
        var f = new RemoteExplorerItem { Name = "startup.sh", FullPath = "/etc/startup.sh", EntryType = RemoteEntryType.File, Size = 3482, Mode = "-rwxr-xr-x", Owner = "root", Group = "root", ModifiedAt = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero) };
        var rows = FileDetails.Rows(f, false); string V(string k) => rows.First(r => r.Label == k).Value;
        Check("rows: path, size with bytes, modified, permissions, owner", V("Path") == "/etc/startup.sh" && V("Size") == "3.4 KB (3,482 bytes)");
        Check("rows: modified label and permissions", V("Modified") == "20 Nov 2026" && V("Permissions") == "-rwxr-xr-x" && V("Owner / group") == "root / root");
        Check("writable: Write mode off", V("Writable").StartsWith("No (Write mode is off"));
        Check("writable: Write mode on is allowed by the app but does not claim the mount", FileDetails.WritableText("/etc/startup.sh", true).StartsWith("Allowed by the app") && FileDetails.WritableText("/etc/startup.sh", true).Contains("mount is checked"));
        Check("writable: protected top-level area is refused whatever the mode", FileDetails.WritableText("/proc/x", true).StartsWith("No (protected") && FileDetails.WritableText("/dev/sda", true).StartsWith("No (protected") && FileDetails.WritableText("/dev/shmem/x", true).StartsWith("Allowed"));
        var link = new RemoteExplorerItem { Name = "l", FullPath = "/etc/l", EntryType = RemoteEntryType.Symlink, SymlinkTarget = "/mnt/x" };
        Check("symlink shows its target and a folder has no size", FileDetails.Rows(link, false).Any(r => r.Label == "Link target" && r.Value == "/mnt/x") && FileDetails.Rows(new RemoteExplorerItem { Name = "d", FullPath = "/d", EntryType = RemoteEntryType.Directory }, false).First(r => r.Label == "Size").Value == "-");
        Check("preview decision from the listing: text, too large, empty, folder", FileDetails.CanPreview(f) == FileDetails.PreviewKind.Text
            && FileDetails.CanPreview(new RemoteExplorerItem { EntryType = RemoteEntryType.File, Size = FileDetails.MaxPreviewBytes + 1 }) == FileDetails.PreviewKind.TooLarge
            && FileDetails.CanPreview(new RemoteExplorerItem { EntryType = RemoteEntryType.File, Size = 0 }) == FileDetails.PreviewKind.Empty
            && FileDetails.CanPreview(new RemoteExplorerItem { EntryType = RemoteEntryType.Directory }) == FileDetails.PreviewKind.NotAFile);
        Check("text preview keeps the first lines and marks the cut", FileDetails.Make(string.Join("\n", Enumerable.Range(1, 100).Select(i => "line" + i))) is { Kind: FileDetails.PreviewKind.Text } p && p.Text.StartsWith("line1\n") && p.Text.Contains("line40") && !p.Text.Contains("line41") && p.Text.EndsWith("..."));
        Check("short text is shown whole without a cut marker, CRLF normalised", FileDetails.Make("a\r\nb\r\n").Text == "a\nb\n");
        Check("NUL bytes mean binary and nothing is shown", FileDetails.Make("ELF\0\0\0abc") is { Kind: FileDetails.PreviewKind.Binary, Text: "" });
        Check("many control or replacement characters mean binary", FileDetails.Make(new string('\uFFFD', 200) + "abc").Kind == FileDetails.PreviewKind.Binary);
        Check("very long single line is cut by characters", FileDetails.Make(new string('x', 10000)).Text.Length <= FileDetails.MaxPreviewChars + 4);
        Check("empty text is Empty", FileDetails.Make("").Kind == FileDetails.PreviewKind.Empty);
        return fail;
    }
}
