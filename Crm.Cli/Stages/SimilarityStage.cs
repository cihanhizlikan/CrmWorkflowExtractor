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

    public static async Task<SimilarityResult> RunAsync(RunFolder folder, RunState state, IReadOnlyList<WorkflowIr> documents,
        UsageEvidence? usage, SimilarityOptions options, ILogger logger, CancellationToken token)
    {
        SimilarityResult result = new SimilarityEngine(options).Run(documents);
        Dictionary<Guid, string> names = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity.Name);
        Dictionary<Guid, string> clusterOf = result.Clusters.SelectMany(cluster => cluster.Members.Select(member => (member.WorkflowId, cluster.ClusterId)))
            .ToDictionary(pair => pair.WorkflowId, pair => pair.ClusterId);

        await folder.WriteJsonAsync(RunPaths.FamiliesJson, new { options, clusters = result.Clusters }, token);

        Sheet clusters = new(SheetNames.Families, "aile", "aile_buyuklugu", "is_akisi", "aile_rolu", "baslangica_benzerlik",
            "zayif_tutarlilik", "birincil_varlik", "kategori", "son_kayitli_calisma", "karar", "is_akisi_id");
        foreach (WorkflowCluster cluster in result.Clusters)
        {
            foreach (ClusterMember member in cluster.Members)
            {
                clusters.Row(names.GetValueOrDefault(cluster.Medoid, cluster.ClusterId), cluster.Members.Count, member.Name,
                    member.WorkflowId == cluster.Medoid ? "başlangıç noktası" : "üye", member.ScoreToMedoid, cluster.LowCohesion,
                    member.PrimaryEntity, member.Category, LastLoggedRun(usage, member.WorkflowId), "", member.WorkflowId);
            }
        }
        state.Sheets[SheetNames.Families] = clusters;

        Sheet pairs = new(SheetNames.Pairs, "sol_ad", "sag_ad", "bilesik", "yapisal", "sozcuksel", "ayni_aile", "baska_adla_yeniden_yazilmis");
        foreach (PairScore pair in result.Pairs)
        {
            pairs.Row(names[pair.Left], names[pair.Right], pair.Combined, pair.Structural, pair.Lexical,
                clusterOf[pair.Left] == clusterOf[pair.Right], pair.Structural >= RebuiltStructural && pair.Lexical < RebuiltLexical);
        }
        state.Sheets[SheetNames.Pairs] = pairs;

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
