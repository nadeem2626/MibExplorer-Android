using MibExplorer.Models.Adaptations;

namespace MibExplorer.Services.Adaptations;

/// <summary>Staged (pending) adaptation changes. Several fields of the same physical key are merged into ONE transaction on top of the value read from the unit. Nothing is sent from here; Discard just forgets the staged list.</summary>
public sealed class AdaptationBatch
{
    public sealed record Staged(AdaptationCatalogItem Item, string Choice, string Raw);
    public readonly List<Staged> Items = new();
    public sealed record Built(List<PhysicalWriteTransaction> Transactions, List<string> Lines, string? Error);

    public static string KeyId(PhysicalStorageKey k) => k.Partition + ":" + k.Key;

    /// <summary>Stage one change, or replace an earlier staged choice for the same item. Returns an error text or null.</summary>
    public string? Stage(AdaptationCatalogItem item, string choice, string currentRaw)
    {
        if (item.Storage.PhysicalKeys.Count != 1) return "Only single-key adaptations can be staged.";
        var p = AdaptationEncoder.Plan(item, currentRaw, choice);
        if (!p.Ok) return p.Error;
        Items.RemoveAll(x => x.Item.Id == item.Id);
        Items.Add(new Staged(item, choice, currentRaw));
        return null;
    }
    public void Discard() => Items.Clear();
    public bool Remove(string itemId) => Items.RemoveAll(x => x.Item.Id == itemId) > 0;

    /// <summary>Builds one transaction per physical key. All fields of a key are planned in order on top of the same unit value. A field that cannot be planned fails the whole build (nothing partial).</summary>
    public Built Build()
    {
        var txs = new List<PhysicalWriteTransaction>(); var lines = new List<string>();
        foreach (var grp in Items.GroupBy(x => KeyId(x.Item.Storage.PhysicalKeys[0])))
        {
            var key = grp.First().Item.Storage.PhysicalKeys[0];
            string orig = grp.First().Raw; string run = orig;
            if (grp.Any(x => x.Raw != orig)) return new Built(new(), new(), "Staged fields of " + grp.Key + " were read at different times. Read the group again and stage again.");
            foreach (var st in grp)
            {
                var p = AdaptationEncoder.Plan(st.Item, run, st.Choice);
                if (!p.Ok) return new Built(new(), new(), st.Item.Label + ": " + p.Error);
                lines.Add(st.Item.Label + ":  " + AdaptationDecoder.Decode(st.Item, run) + "  >  " + st.Choice);
                run = p.NewRaw;
            }
            var tx = AdaptationEncoder.Transaction(key, orig, run);
            if (tx.IsDirty) txs.Add(tx);
        }
        return new Built(txs, lines, null);
    }
}
