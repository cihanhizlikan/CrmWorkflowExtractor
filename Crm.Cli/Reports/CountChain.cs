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
        string verdict = Holds ? "ok" : string.Create(CultureInfo.InvariantCulture, $"GAP of {Left - Right}");
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
                links.Add(new CountLink("records", inventory.ApiCount, "$count", inventory.Retrieved, "retrieved"));
            }
            links.Add(new CountLink("classified", inventory.Retrieved, "retrieved",
                inventory.Definitions + inventory.Activations + inventory.Templates + inventory.OtherType, "definitions + activations + templates + other"));
            if (counts.ContainsKey("xaml.written"))
            {
                links.Add(new CountLink("xaml", inventory.Definitions + inventory.Activations, "definitions + activations",
                    Get(counts, "xaml.written") + Get(counts, "xaml.withoutXaml") + Get(counts, "xaml.failed"), "xaml written + without xaml + failed"));
            }
        }
        if (counts.ContainsKey("ir.documents") && counts.ContainsKey("xaml.definitions"))
        {
            links.Add(new CountLink("ir", Get(counts, "xaml.definitions"), "definition xaml files",
                Get(counts, "ir.documents") + Get(counts, "manualReview") + Get(counts, "ir.parseFailed") + Get(counts, "ir.noInventoryRecord"),
                "ir documents + manual review + parse failures + orphaned xaml"));
        }
        if (counts.ContainsKey("bpmn.written"))
        {
            links.Add(new CountLink("bpmn", Get(counts, "ir.documents"), "ir documents", Get(counts, "bpmn.written"), "bpmn files"));
        }
        if (counts.ContainsKey("clusters.total"))
        {
            links.Add(new CountLink("clusters", Get(counts, "ir.documents"), "ir documents", Get(counts, "clusters.members"), "workflows placed in a cluster"));
        }
        return links;
    }

    private static int Get(IReadOnlyDictionary<string, int> counts, string key)
    {
        return counts.TryGetValue(key, out int value) ? value : 0;
    }
}
