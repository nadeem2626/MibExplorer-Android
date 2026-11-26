using MibExplorer.Models.Adaptations; using MibExplorer.Services.Adaptations;
public static class AdaptRealTests
{
    public static Task<int> RunAsync()
    {
        var svc = new AdaptationCatalogService();
        Console.WriteLine("GATING: wrong id -> " + (svc.LoadForAsam("EV_Nope", "001000") == null) + "; short version -> " + (svc.LoadForAsam("EV_MUEnt4CGen2LGE", "00") == null) + "; wrong prefix -> " + (svc.LoadForAsam("EV_MUEnt4CGen2LGE", "999000") == null) + "; empty -> " + (svc.LoadForAsam("", "001000") == null));
        var idx = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../MibExplorer.Core/Data/Adaptations/Odx/index.json")));
        int totalEdit = 0, totalMapped = 0, totalItems = 0, rt = 0;
        foreach (var v in idx.RootElement.GetProperty("variants").EnumerateArray())
        {
            var c = svc.LoadForAsam(v.GetProperty("identifier").GetString()!, v.GetProperty("versionPrefix").GetString()! + "000");
            if (c == null) { Console.WriteLine("LOAD FAIL " + v.GetProperty("variant")); rt++; continue; }
            var its = c.Groups.SelectMany(g => g.Adaptations).ToList();
            int ed = its.Count(a => a.Ui.Editable), mp = its.Count(a => a.Storage.Mapped), em = its.Count(a => a.Ui.Editable && a.Storage.Mapped);
            totalEdit += em; totalMapped += mp; totalItems += its.Count;
            Console.WriteLine($"ODX {c.Name}: groups={c.Groups.Count} items={its.Count} (index {v.GetProperty("itemCount").GetInt32()}) editable={ed} mapped={mp} editable+mapped={em}");
            if (its.Count != v.GetProperty("itemCount").GetInt32()) rt++;
        }
        Console.WriteLine($"ODX totals items={totalItems} mapped={totalMapped} editable+mapped={totalEdit} loadproblems={rt}");
        var cat = new AdaptationCatalogService().Load();
        var items = cat.Groups.SelectMany(g => g.Adaptations.Select(a => (g, a))).ToList();
        Console.WriteLine($"catalog {cat.SchemaVersion} ecu {cat.Ecu} groups {cat.Groups.Count} items {items.Count}");
        var modes = items.GroupBy(x => (x.a.Storage.Mapped, x.a.Storage.Mode ?? "-", x.a.Ui.Editable)).OrderByDescending(g => g.Count());
        foreach (var m in modes) Console.WriteLine($"  mapped={m.Key.Mapped} mode={m.Key.Item2} editable={m.Key.Editable}: {m.Count()}");
        int fail = 0, planned = 0, refused = 0; var reasons = new Dictionary<string, int>();
        foreach (var (g, a) in items.Where(x => x.a.Ui.Editable))
        {
            foreach (var v in a.Ui.Values)
            {
                string cur = a.Storage.Mode switch { "packedFlags" => "0", "blobBit" or "blobBitsEnum" => "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00", _ => "0" };
                var r = AdaptationEncoder.Plan(a, cur, v.Label);
                if (r.Ok) { planned++; var back = AdaptationDecoder.Decode(a, r.NewRaw); if (a.Storage.Mode is "packedFlags" or "blobBit" or "blobBitsEnum" or "scalarEnum" && !string.Equals(back, v.Label, StringComparison.OrdinalIgnoreCase) && back != "-") { fail++; if (fail < 8) Console.WriteLine($"  ROUNDTRIP MISMATCH {a.Id} '{v.Label}' -> raw {r.NewRaw} -> '{back}'"); } }
                else { refused++; reasons[r.Error.Length > 70 ? r.Error[..70] : r.Error] = reasons.GetValueOrDefault(r.Error.Length > 70 ? r.Error[..70] : r.Error) + 1; }
            }
        }
        Console.WriteLine($"editable value plans: ok={planned} refused={refused} roundtrip-mismatch={fail}");
        foreach (var kv in reasons.OrderByDescending(k => k.Value).Take(8)) Console.WriteLine($"  refused x{kv.Value}: {kv.Key}");
        int unmappedEditable = items.Count(x => x.a.Ui.Editable && !x.a.Storage.Mapped);
        Console.WriteLine("editable but unmapped (cannot be written, Plan refuses): " + unmappedEditable);
        return Task.FromResult(fail == 0 ? 0 : 1);
    }
}
