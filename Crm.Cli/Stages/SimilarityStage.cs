using Crm.Cli.Reports;
using Crm.Extract.Runs;
using Crm.Ir.Model;
using Crm.Similarity;
using Microsoft.Extensions.Logging;

namespace Crm.Cli.Stages;

/// <summary>M5: <c>clusters/clusters.json</c>, <c>clusters.csv</c> (the file the architects open) and <c>pairs.csv</c> (the audit trail).</summary>
public static class SimilarityStage
{
    /// <summary>A pair this structurally close under names this different was probably rebuilt rather than copied (§7.3).</summary>
    private const double RebuiltStructural = 0.95;
    private const double RebuiltLexical = 0.5;

    public static async Task<SimilarityResult> RunAsync(RunFolder folder, RunState state, IReadOnlyList<WorkflowIr> documents, IReadOnlyList<WorkflowIr> drafts, IReadOnlyList<WorkflowIr> supplied,
        UsageEvidence? usage, SimilarityOptions options, ILogger logger, CancellationToken token)
    {
        SimilarityResult result = new SimilarityEngine(options).Run(documents);
        Dictionary<Guid, string> names = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity.Name);
        Dictionary<Guid, string> clusterOf = result.Clusters.SelectMany(cluster => cluster.Members.Select(member => (member.WorkflowId, cluster.ClusterId)))
            .ToDictionary(pair => pair.WorkflowId, pair => pair.ClusterId);

        await folder.WriteJsonAsync(RunPaths.FamiliesJson, new { options, clusters = result.Clusters }, token);

        ExcelCsv clusters = new("aile_id", "aile_buyuklugu", "is_akisi", "is_akisi_id", "birincil_varlik", "kategori", "durum",
            "baslangica_benzerlik", "baslangic_noktasi", "zayif_tutarlilik", "adi_deneme_gibi", "son_kayitli_calisma", "karar");
        foreach (WorkflowCluster cluster in result.Clusters)
        {
            foreach (ClusterMember member in cluster.Members)
            {
                clusters.Row(cluster.ClusterId, cluster.Members.Count, member.Name, member.WorkflowId, member.PrimaryEntity, member.Category, member.State,
                    member.ScoreToMedoid, member.WorkflowId == cluster.Medoid, cluster.LowCohesion, UsageStage.NameSuggestsTest(member.Name),
                    LastLoggedRun(usage, member.WorkflowId), "");
            }
        }
        await folder.WriteBytesAsync(RunPaths.FamiliesCsv, clusters.ToBytes(), token);

        ExcelCsv held = new("is_akisi", "is_akisi_id", "birincil_varlik", "kategori", "adi_deneme_gibi", "degistirilme");
        foreach (WorkflowIr draft in drafts.OrderBy(draft => draft.Identity.Name, StringComparer.Ordinal))
        {
            held.Row(draft.Identity.Name, draft.Identity.WorkflowId, draft.Identity.PrimaryEntity, draft.Identity.Category,
                UsageStage.NameSuggestsTest(draft.Identity.Name), draft.Identity.ModifiedOn);
        }
        await folder.WriteBytesAsync(RunPaths.DraftsCsv, held.ToBytes(), token);
        state.Counts["clusters.draftsHeldApart"] = drafts.Count;

        ExcelCsv shipped = new("is_akisi", "is_akisi_id", "birincil_varlik", "kategori", "durum", "degistirilme");
        foreach (WorkflowIr document in supplied.OrderBy(document => document.Identity.Name, StringComparer.Ordinal))
        {
            shipped.Row(document.Identity.Name, document.Identity.WorkflowId, document.Identity.PrimaryEntity,
                document.Identity.Category, document.Identity.State, document.Identity.ModifiedOn);
        }
        await folder.WriteBytesAsync(RunPaths.SuppliedCsv, shipped.ToBytes(), token);
        state.Counts["clusters.suppliedHeldApart"] = supplied.Count;

        ExcelCsv pairs = new("sol_id", "sol_ad", "sag_id", "sag_ad", "bilesik", "yapisal", "sozcuksel", "yol_jaccard", "parca_jaccard",
            "ad_oneki", "sozcuk_jaccard", "jaro_winkler", "ayni_aile", "baska_adla_yeniden_yazilmis");
        foreach (PairScore pair in result.Pairs)
        {
            pairs.Row(pair.Left, names[pair.Left], pair.Right, names[pair.Right], pair.Combined, pair.Structural, pair.Lexical, pair.PathJaccard,
                pair.ShingleJaccard, pair.Prefix, pair.TokenJaccard, pair.JaroWinkler, clusterOf[pair.Left] == clusterOf[pair.Right],
                pair.Structural >= RebuiltStructural && pair.Lexical < RebuiltLexical);
        }
        await folder.WriteBytesAsync(RunPaths.PairsCsv, pairs.ToBytes(), token);

        state.Counts["clusters.total"] = result.Clusters.Count;
        state.Counts["clusters.members"] = result.Clusters.Sum(cluster => cluster.Members.Count);
        state.Similarity = result;
        state.Counts["clusters.families"] = result.Clusters.Count(cluster => cluster.Members.Count > 1);
        state.Counts["clusters.lowCohesion"] = result.Clusters.Count(cluster => cluster.LowCohesion);
        state.Counts["pairs.reported"] = result.Pairs.Count;
        state.StagesRun.Add(RunStages.Similarity);
        logger.LogInformation("Similarity: {Families} families of 2+, {Clusters} clusters in total, {Pairs} pairs above the floor",
            state.Counts["clusters.families"], result.Clusters.Count, result.Pairs.Count);
        return result;
    }

    private static string LastLoggedRun(UsageEvidence? usage, Guid workflowId)
    {
        return usage?.Workflows.GetValueOrDefault(workflowId)?.LastLoggedRun is DateTimeOffset last
            ? last.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : "";
    }
}
