using MibExplorer.Services;
public static class LsDateTests
{
    public static int Run()
    {
        int fail = 0; void Check(string n, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + n); if (!ok) fail++; }
        var today = new DateTime(2026, 11, 25);
        Check("recent time form gets the current year", LsDate.Label(LsDate.Parse("Nov", "24", "12:30", today)) == "24 Nov 2026");
        Check("year form", LsDate.Label(LsDate.Parse("Mar", "3", "2025", today)) == "03 Mar 2025");
        Check("time form later in the year than today belongs to last year", LsDate.Label(LsDate.Parse("Dec", "20", "09:00", today)) == "20 Dec 2025");
        Check("tomorrow with a time stays this year (clock skew)", LsDate.Label(LsDate.Parse("Nov", "26", "01:00", today)) == "26 Nov 2026");
        Check("Feb 29 with a time in a non-leap year does not throw", LsDate.Parse("Feb", "29", "10:00", today) is null);
        Check("bad month, day, time and year give null", LsDate.Parse("Foo", "1", "10:00", today) is null && LsDate.Parse("Jan", "40", "10:00", today) is null && LsDate.Parse("Jan", "1", "xx:yy", today) is null && LsDate.Parse("Jan", "1", "1800", today) is null && LsDate.Parse("Feb", "30", "2025", today) is null);
        Check("null gives empty label", LsDate.Label(null) == "");
        var items = SshMibConnectionService_Test.Parse();
        Check("ls -la parsing fills ModifiedAt for files, folders and keeps names with spaces", items.Count == 3 && items.All(i => i.ModifiedAt != null) && items.Any(i => i.Name == "my file.txt"));
        return fail;
    }
}
public static class SshMibConnectionService_Test
{
    public static IReadOnlyList<MibExplorer.Models.RemoteExplorerItem> Parse() =>
        MibExplorer.Services.SshMibConnectionService.ParseLsLa("/etc", string.Join("\n", "total 8",
            "drwxr-xr-x    2 root     root          4096 Nov 24 12:30 mnt",
            "-rw-r--r--    1 root     root          1234 Nov 20  2025 startup.sh",
            "-rw-r--r--    1 root     root           812 Jan  3 08:00 my file.txt"));
}
