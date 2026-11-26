using MibExplorer.Models; using MibExplorer.Services;
public static class FileListViewTests
{
    static RemoteExplorerItem F(string n, long size, int day, bool dir = false) => new() { Name = n, FullPath = "/x/" + n, EntryType = dir ? RemoteEntryType.Directory : RemoteEntryType.File, Size = size, ModifiedAt = day == 0 ? null : new DateTimeOffset(2026, 11, day, 0, 0, 0, TimeSpan.Zero) };
    public static int Run()
    {
        int fail = 0; void Check(string n, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + n); if (!ok) fail++; }
        var items = new[] { F("b.txt", 300, 20), F("A.txt", 100, 24), F("zdir", 0, 10, true), F("c.txt", 200, 0), F("adir", 0, 22, true) };
        string N(IEnumerable<RemoteExplorerItem> l) => string.Join(",", l.Select(i => i.Name));
        Check("name sort: folders first, case-insensitive", N(FileListView.Sort(items, SortKey.Name)) == "adir,zdir,A.txt,b.txt,c.txt");
        Check("name sort descending keeps folders first", N(FileListView.Sort(items, SortKey.Name, true)) == "zdir,adir,c.txt,b.txt,A.txt");
        Check("modified sort: unknown date is oldest, folders first", N(FileListView.Sort(items, SortKey.Modified)) == "zdir,adir,c.txt,b.txt,A.txt");
        Check("size sort ascending", N(FileListView.Sort(items, SortKey.Size)) == "adir,zdir,A.txt,c.txt,b.txt");
        Check("size sort descending", N(FileListView.Sort(items, SortKey.Size, true)) == "adir,zdir,b.txt,c.txt,A.txt");
        Check("filter is case-insensitive substring", N(FileListView.Filter(items, "TXT")) == "b.txt,A.txt,c.txt" && N(FileListView.Filter(items, "dir")) == "zdir,adir");
        Check("empty, blank and null filter keep everything", FileListView.Filter(items, "").Count() == 5 && FileListView.Filter(items, "  ").Count() == 5 && FileListView.Filter(items, null).Count() == 5);
        Check("filter with no match is empty", !FileListView.Filter(items, "nope").Any());
        Check("sort key cycle Name > Modified > Size > Name", FileListView.NextKey(SortKey.Name) == SortKey.Modified && FileListView.NextKey(SortKey.Modified) == SortKey.Size && FileListView.NextKey(SortKey.Size) == SortKey.Name);
        var bc = FileListView.Breadcrumb("/net/mmx/mnt");
        Check("breadcrumb: root plus each folder with absolute path", bc.Count == 4 && bc[0].Path == "/" && bc[1] == new FileListView.Crumb("net", "/net") && bc[3].Path == "/net/mmx/mnt");
        Check("breadcrumb of root, null and doubled slashes", FileListView.Breadcrumb("/").Count == 1 && FileListView.Breadcrumb(null).Count == 1 && FileListView.Breadcrumb("//a//b/").Last().Path == "/a/b");
        return fail;
    }
}
