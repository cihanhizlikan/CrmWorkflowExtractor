using System.Globalization;
using Crm.Extract.Inventory;

namespace Crm.Cli.Reports;

/// <summary>One equation of the §8 count chain: the stage count on the left must equal the explained sum on the right.</summary>
public sealed record CountLink(string Name, int Left, string LeftLabel, int Right, string RightLabel)
{
    public bool Holds
    {
        get { return Left == Right; }
    }

    public override string ToString()
    {
        string verdict = Holds ? "uygun" : string.Create(CultureInfo.InvariantCulture, $"EKSİK: {Left - Right}");
        return string.Create(CultureInfo.InvariantCulture, $"{Name}: {LeftLabel} {Left} = {RightLabel} {Right} — {verdict}");
    }
}

/// <summary>
/// §8: API <c>$count</c> → records → XAML files → IR documents → BPMN files, with every gap assigned to a named
/// bucket (templates, manual review, parse failures, …). Any link that does not balance is an UNEXPLAINED gap and
/// fails the run. A link whose stage did not run is omitted, never reported as balanced.
/// </summary>
public static class CountChain
{
    public static IReadOnlyList<CountLink> Evaluate(RunState state)
    {
        List<CountLink> links = [];
        IReadOnlyDictionary<string, int> counts = state.Counts;
        if (state.Reconciliation?.Counts is InventoryCounts inventory)
        {
            if (inventory.ApiCount >= 0)
            {
                // With no server count (-1) there is nothing to balance; the reconciliation already warned, loudly.
                links.Add(new CountLink("kayıtlar", inventory.ApiCount, "$count", inventory.Retrieved, "alınan"));
            }
            links.Add(new CountLink("sınıflandırma", inventory.Retrieved, "alınan",
                inventory.Definitions + inventory.Activations + inventory.Templates + inventory.OtherType, "tanım + etkinleştirme + şablon + diğer"));
            if (counts.ContainsKey("xaml.written"))
            {
                links.Add(new CountLink("xaml", inventory.Definitions + inventory.Activations, "tanım + etkinleştirme",
                    Get(counts, "xaml.written") + Get(counts, "xaml.withoutXaml") + Get(counts, "xaml.failed"), "yazılan + xaml içermeyen + başarısız"));
            }
        }
        if (counts.ContainsKey("ir.documents") && counts.ContainsKey("xaml.definitions"))
        {
            links.Add(new CountLink("ara model", Get(counts, "xaml.definitions"), "tanım xaml dosyası",
                Get(counts, "ir.documents") + Get(counts, "manualReview") + Get(counts, "ir.parseFailed") + Get(counts, "ir.noInventoryRecord"),
                "ara model + elle inceleme + ayrıştırma hatası + karşılıksız xaml"));
        }
        if (counts.ContainsKey("bpmn.written"))
        {
            links.Add(new CountLink("bpmn", Get(counts, "ir.documents"), "ara model belgesi", Get(counts, "bpmn.written"), "bpmn dosyası"));
        }
        if (counts.ContainsKey("clusters.total"))
        {
            links.Add(new CountLink("aileler", Get(counts, "ir.documents"), "ara model belgesi",
                Get(counts, "clusters.members") + Get(counts, "clusters.draftsHeldApart") + Get(counts, "clusters.suppliedHeldApart")
                    + Get(counts, "clusters.testNamedHeldApart"),
                "aileye yerleşen + ayrı tutulan taslak + ürünle gelen + hiç çalışmamış deneme"));
        }
        return links;
    }

    private static int Get(IReadOnlyDictionary<string, int> counts, string key)
    {
        return counts.TryGetValue(key, out int value) ? value : 0;
    }
}
