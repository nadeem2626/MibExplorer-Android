using MibExplorer.Models.Adaptations;
using MibExplorer.Services.Adaptations;

public static class AdaptationTests
{
    static int pass, fail;
    static void Check(string n, bool ok, string? info = null) { if (!ok && info != null) Console.WriteLine("  got: " + info); if (ok) { pass++; Console.WriteLine("PASS " + n); } else { fail++; Console.WriteLine("FAIL " + n); } }
    static AdaptationCatalogEnumValue V(string l, string? r = null) => new() { Label = l, Raw = r };
    static AdaptationCatalogItem I(string mode, AdaptationCatalogStorage? st = null, params AdaptationCatalogEnumValue[] ui) => new()
    {
        Ui = new AdaptationCatalogItemUi { Values = ui.ToList() },
        Storage = st ?? new AdaptationCatalogStorage { Mapped = true, Mode = mode },
    };
    public static Task<int> RunAsync()
    {
        var on = V("activated"); var off = V("not activated");
        Check("scalar label match", AdaptationDecoder.Decode(I("scalar", null, V("Metric", "1"), V("Imperial", "2")), "2") == "Imperial");
        Check("scalar unknown raw is shown as is", AdaptationDecoder.Decode(I("scalar", null, V("A", "1")), "9") == "9");
        Check("empty is dash", AdaptationDecoder.Decode(I("scalar"), " ") == "-");
        Check("unmapped uses catalog", AdaptationDecoder.Decode(I("x", new AdaptationCatalogStorage { Mapped = false }, V("A", "1")), "1") == "A");
        Check("packedFlags set", AdaptationDecoder.Decode(I("packedFlags", new AdaptationCatalogStorage { Mapped = true, Mode = "packedFlags", Mask = 4 }, on, off), "5") == "activated");
        Check("packedFlags clear", AdaptationDecoder.Decode(I("packedFlags", new AdaptationCatalogStorage { Mapped = true, Mode = "packedFlags", Mask = 4 }, on, off), "3") == "not activated");
        var bb = new AdaptationCatalogStorage { Mapped = true, Mode = "blobBit", ByteIndex = 1, BitIndex = 3 };
        Check("blobBit set (spaced hex)", AdaptationDecoder.Decode(I("blobBit", bb, on, off), "00 08 00") == "activated");
        Check("blobBit clear (compact hex)", AdaptationDecoder.Decode(I("blobBit", bb, on, off), "000000") == "not activated");
        Check("blobBit index outside blob falls back to raw", AdaptationDecoder.Decode(I("blobBit", new AdaptationCatalogStorage { Mapped = true, Mode = "blobBit", ByteIndex = 9, BitIndex = 0 }, on, off), "0001") == "0001");
        Check("blobBit from hexdump line", AdaptationDecoder.Decode(I("blobBit", new AdaptationCatalogStorage { Mapped = true, Mode = "blobBit", ByteIndex = 0, BitIndex = 0 }, on, off), "00000000: 01 02 | ..") == "activated");
        var be = new AdaptationCatalogStorage { Mapped = true, Mode = "blobBitsEnum", ByteIndex = 0, BitIndex = 6, BitWidth = 4, Values = new() { V("Low", "3"), V("High", "5") } };
        // bits 6..9 of bytes [0xC0, 0x01] = 11 (bits 6,7 of b0) + 01 (bits 0,1 of b1 = 1,0) -> value = 0b0111? bit6=1,bit7=1,bit8=1,bit9=0 => 7
        Check("blobBitsEnum across bytes extracts 7", AdaptationDecoder.TryExtractBitRange(new byte[] { 0xC0, 0x01 }, 0, 6, 4, out int v7) && v7 == 7);
        be = new AdaptationCatalogStorage { Mapped = true, Mode = "blobBitsEnum", ByteIndex = 0, BitIndex = 0, BitWidth = 3, Values = new() { V("Low", "3"), V("High", "5") } };
        Check("blobBitsEnum label", AdaptationDecoder.Decode(I("blobBitsEnum", be), "05") == "High");
        Check("blobBitsEnum no label shows number", AdaptationDecoder.Decode(I("blobBitsEnum", be), "07") == "7");
        Check("extract out of range fails", !AdaptationDecoder.TryExtractBitRange(new byte[] { 1 }, 0, 6, 4, out _));
        Check("extract width 32 rejected", !AdaptationDecoder.TryExtractBitRange(new byte[8], 0, 0, 32, out _));
        Check("hex garbage returns null", AdaptationDecoder.ParseHex("zz") is null);
        Check("hex odd compact returns null", AdaptationDecoder.ParseHex("abc") is null);
        {
            var key = new PhysicalStorageKey("p", "k", "t");
            AdaptationCatalogItem F(string id, int mask) => new() { Id = id, Label = id, Ui = new AdaptationCatalogItemUi { Editable = true, Values = new() { on, off } }, Storage = new AdaptationCatalogStorage { Mapped = true, Mode = "packedFlags", Mask = mask, Partition = "p", Key = "k", Type = "t" } };
            var b = new AdaptationBatch(); var a1 = F("a", 1); var a2 = F("b", 4);
            Check("batch stages two fields", b.Stage(a1, "activated", "0") == null && b.Stage(a2, "activated", "0") == null);
            var bt = b.Build();
            Check("batch: two fields of one key merge into ONE transaction", bt.Error == null && bt.Transactions.Count == 1 && bt.Transactions[0].MergedRawValue == "5" && bt.Transactions[0].OriginalRawValue == "0" && bt.Lines.Count == 2);
            Check("batch: restaging same item replaces it", b.Stage(a1, "not activated", "0") == null && b.Items.Count == 2 && b.Build().Transactions[0].MergedRawValue == "4");
            Check("batch: stage with wrong label refused", new AdaptationBatch().Stage(a1, "banana", "0") != null);
            Check("batch: unread value refused", new AdaptationBatch().Stage(a1, "activated", "") != null);
            var b2 = new AdaptationBatch(); b2.Stage(a1, "activated", "0"); b2.Stage(a2, "activated", "1");
            Check("batch: fields read at different times refuse to build", b2.Build().Error != null);
            b.Discard(); Check("batch: discard empties and builds nothing", b.Items.Count == 0 && b.Build().Transactions.Count == 0);
            var b3 = new AdaptationBatch(); b3.Stage(a1, "activated", "1"); Check("batch: no-op change builds no transaction", b3.Build().Transactions.Count == 0);
        }
        {
            var e1 = I("packedFlags", new AdaptationCatalogStorage { Mapped = true, Mode = "packedFlags", Mask = 4 }, on, off); e1.Ui.GetType();
            var ed = new AdaptationCatalogItem { Ui = new AdaptationCatalogItemUi { Editable = true, Values = new() { on, off } }, Storage = new AdaptationCatalogStorage { Mapped = true, Mode = "packedFlags", Mask = 4 } };
            var r = AdaptationEncoder.Plan(ed, "1", "activated"); Check("packedFlags plan sets bit", r.Ok && r.NewRaw == "5");
            Check("packedFlags round trip", AdaptationDecoder.Decode(ed, r.NewRaw) == "activated");
            Check("packedFlags clears bit and keeps others", AdaptationEncoder.Plan(ed, "7", "not activated").NewRaw == "3");
            Check("not editable is refused", !AdaptationEncoder.Plan(e1, "1", "activated").Ok);
            Check("label not in the catalog is refused", !AdaptationEncoder.Plan(ed, "1", "banana").Ok);
            Check("unread value is refused", !AdaptationEncoder.Plan(ed, "", "activated").Ok);
            var eb = new AdaptationCatalogItem { Ui = new AdaptationCatalogItemUi { Editable = true, Values = new() { on, off } }, Storage = new AdaptationCatalogStorage { Mapped = true, Mode = "blobBit", ByteIndex = 1, BitIndex = 3 } };
            var rb = AdaptationEncoder.Plan(eb, "00 F0 0A", "activated"); Check("blobBit sets only its bit, others kept", rb.Ok && rb.NewRaw == "00 F8 0A");
            Check("blobBit round trip", AdaptationDecoder.Decode(eb, rb.NewRaw) == "activated");
            Check("blobBit clear", AdaptationEncoder.Plan(eb, "00 F8 0A", "not activated").NewRaw == "00 F0 0A");
            Check("blobBit outside blob refused", !AdaptationEncoder.Plan(eb, "00", "activated").Ok);
            var ee = new AdaptationCatalogItem { Ui = new AdaptationCatalogItemUi { Editable = true, Values = new() { V("Low", "3"), V("High", "5") } }, Storage = new AdaptationCatalogStorage { Mapped = true, Mode = "blobBitsEnum", ByteIndex = 0, BitIndex = 6, BitWidth = 4, Values = new() { V("Low", "3"), V("High", "5") } } };
            var re = AdaptationEncoder.Plan(ee, "3F 06", "High"); Check("blobBitsEnum across bytes keeps other bits", re.Ok && re.NewRaw == "7F 05", re.NewRaw);
            Check("blobBitsEnum round trip", AdaptationDecoder.Decode(ee, re.NewRaw) == "High");
            var se = new AdaptationCatalogItem { Ui = new AdaptationCatalogItemUi { Editable = true, Values = new() { V("A", "1") } }, Storage = new AdaptationCatalogStorage { Mapped = true, Mode = "scalarEnum", Values = new() { V("A", "9") } } };
            Check("scalarEnum uses the storage value", AdaptationEncoder.Plan(se, "0", "A").NewRaw == "9");
            Check("a bit range value too big is refused", !AdaptationEncoder.TrySetBitRange(new byte[2], 0, 0, 2, 4));
        }
        {
            var k = AdaptationRestore.ParseKey("0:0xC0030004:blob"); Check("restore: key parsed", k is not null && k.Partition == "0" && k.Key == "0xC0030004" && k.Type == "blob");
            Check("restore: bad keys refused", AdaptationRestore.ParseKey("a:b") is null && AdaptationRestore.ParseKey("0:x;rm:blob") is null && AdaptationRestore.ParseKey("0::blob") is null && AdaptationRestore.ParseKey("0:a b:blob") is null);
            var en = new AdaptationHistoryEntry { PhysicalKey = "0:0x1:blob", OriginalRawValue = "00 F0" };
            var pl = AdaptationRestore.Plan(en, "00 F8"); Check("restore: plan sets merged to the saved value", pl.Tx is not null && pl.Tx.OriginalRawValue == "00 F8" && pl.Tx.MergedRawValue == "00 F0" && pl.Tx.IsDirty);
            Check("restore: already equal means nothing to do", AdaptationRestore.Plan(en, "00 F0").Tx is null);
            Check("restore: unread live value refused", AdaptationRestore.Plan(en, "").Tx is null);
            Check("restore: empty saved value refused", AdaptationRestore.Plan(new AdaptationHistoryEntry { PhysicalKey = "0:0x1:blob" }, "00").Tx is null);
            Check("restore: control char in saved value refused", AdaptationRestore.Plan(new AdaptationHistoryEntry { PhysicalKey = "0:0x1:blob", OriginalRawValue = "00\n01" }, "00").Tx is null);
        }
        var h = new AdaptationHistoryService(); AdaptationHistoryService.RootOverride = Path.Combine(Path.GetTempPath(), "ah_" + Guid.NewGuid().ToString("N"));
        Check("history folder honours root override", h.GetVehicleHistoryFolder("VIN123").StartsWith(AdaptationHistoryService.RootOverride!));
        Check("history folder name is safe", !h.GetVehicleHistoryFolder("../../x").Contains(".."));
        Console.WriteLine($"adaptations: {pass} pass, {fail} fail");
        return Task.FromResult(fail == 0 ? 0 : 1);
    }
}
