using MibExplorer.Services.Editing;

public static class EditingTests
{
    static int pass, fail;
    static void Check(string n, bool ok) { if (ok) { pass++; Console.WriteLine("PASS " + n); } else { fail++; Console.WriteLine("FAIL " + n); } }
    static string Apply(List<DiffLine> d) => string.Join("\n", d.Where(x => x.Kind != DiffKind.Removed).Select(x => x.Text));
    static string Undo(List<DiffLine> d) => string.Join("\n", d.Where(x => x.Kind != DiffKind.Added).Select(x => x.Text));

    public static Task<int> RunAsync()
    {
        Check("identical = no changes", TextDiff.ChangedCount(TextDiff.Compute("a\nb\n", "a\nb\n")) == 0);
        var d = TextDiff.Compute("a\nb\nc\n", "a\nX\nc\n");
        Check("one modified line = 1 removed + 1 added", d.Count(x => x.Kind == DiffKind.Removed) == 1 && d.Count(x => x.Kind == DiffKind.Added) == 1);
        Check("old line numbers", d.First(x => x.Kind == DiffKind.Removed).OldLine == 2 && d.First(x => x.Kind == DiffKind.Added).NewLine == 2);
        d = TextDiff.Compute("a\nb\n", "a\nb\nc\nd\n");
        Check("append two lines", d.Count(x => x.Kind == DiffKind.Added) == 2 && d.Count(x => x.Kind == DiffKind.Removed) == 0);
        d = TextDiff.Compute("", "x\n"); Check("empty to one line", d.Count == 1 && d[0].Kind == DiffKind.Added);
        d = TextDiff.Compute("x\n", ""); Check("one line to empty", d.Count == 1 && d[0].Kind == DiffKind.Removed);
        Check("ignore whitespace hides indent change", TextDiff.ChangedCount(TextDiff.Compute("a b\n", "  a   b\n", true)) == 0 && TextDiff.ChangedCount(TextDiff.Compute("a b\n", "  a   b\n", false)) == 2);
        var rnd = new Random(7); int bad = 0;
        for (int t = 0; t < 300; t++)
        {
            var A = string.Join("\n", Enumerable.Range(0, rnd.Next(0, 25)).Select(_ => "l" + rnd.Next(0, 6))) + "\n";
            var B = string.Join("\n", Enumerable.Range(0, rnd.Next(0, 25)).Select(_ => "l" + rnd.Next(0, 6))) + "\n";
            var df = TextDiff.Compute(A, B);
            if (Apply(df) != string.Join("\n", TextDiff.SplitLines(B)) || Undo(df) != string.Join("\n", TextDiff.SplitLines(A))) bad++;
        }
        Check("300 random diffs: applying/undoing reproduces both texts", bad == 0);
        Check("binary detected", TextDiff.LooksBinary(new byte[] { 65, 0, 66 }) && !TextDiff.LooksBinary(System.Text.Encoding.UTF8.GetBytes("hello\n")));
        var raw = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes("a\r\nb\r\n")).ToArray();
        var dec = TextDiff.Decode(raw);
        Check("BOM and CRLF detected", dec.Bom && dec.Eol == "\r\n" && dec.Text == "a\nb\n");
        Check("encode restores identical bytes", TextDiff.Encode(dec.Text, dec.Bom, dec.Eol).SequenceEqual(raw));
        var lf = System.Text.Encoding.UTF8.GetBytes("é\n"); var d2 = TextDiff.Decode(lf);
        Check("utf8 LF round trip", TextDiff.Encode(d2.Text, d2.Bom, d2.Eol).SequenceEqual(lf));
        Console.WriteLine($"editing: {pass} pass, {fail} fail");
        return Task.FromResult(fail == 0 ? 0 : 1);
    }
}
