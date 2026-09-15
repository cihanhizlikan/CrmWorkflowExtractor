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

    public static async Task<SimilarityResult> RunAsync(RunFolder folder, RunState state, IReadOnlyList<WorkflowIr> documents, SimilarityOptions options, ILogger logger, CancellationToken token)
    {
        SimilarityResult result = new SimilarityEngine(options).Run(documents);
        Dictionary<Guid, string> names = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity.Name);
        Dictionary<Guid, string> clusterOf = result.Clusters.SelectMany(cluster => cluster.Members.Select(member => (member.WorkflowId, cluster.ClusterId)))
            .ToDictionary(pair => pair.WorkflowId, pair => pair.ClusterId);

        await folder.WriteJsonAsync("clusters/clusters.json", new { options, clusters = result.Clusters }, token);

        ExcelCsv clusters = new("cluster_id", "cluster_size", "workflow_name", "workflow_id", "primary_entity", "category", "state",
            "score_to_medoid", "is_medoid", "low_cohesion", "decision");
        foreach (WorkflowCluster cluster in result.Clusters)
        {
            foreach (ClusterMember member in cluster.Members)
            {
                clusters.Row(cluster.ClusterId, cluster.Members.Count, member.Name, member.WorkflowId, member.PrimaryEntity, member.Category, member.State,
                    member.ScoreToMedoid, member.WorkflowId == cluster.Medoid, cluster.LowCohesion, "");
            }
        }
        await folder.WriteBytesAsync("clusters/clusters.csv", clusters.ToBytes(), token);

        ExcelCsv pairs = new("left_id", "left_name", "right_id", "right_name", "combined", "structural", "lexical", "path_jaccard", "shingle_jaccard",
            "name_prefix", "token_jaccard", "jaro_winkler", "same_cluster", "rebuilt_under_other_name");
        foreach (PairScore pair in result.Pairs)
        {
            pairs.Row(pair.Left, names[pair.Left], pair.Right, names[pair.Right], pair.Combined, pair.Structural, pair.Lexical, pair.PathJaccard,
                pair.ShingleJaccard, pair.Prefix, pair.TokenJaccard, pair.JaroWinkler, clusterOf[pair.Left] == clusterOf[pair.Right],
                pair.Structural >= RebuiltStructural && pair.Lexical < RebuiltLexical);
        }
        await folder.WriteBytesAsync("clusters/pairs.csv", pairs.ToBytes(), token);

        state.Counts["clusters.total"] = result.Clusters.Count;
        state.Counts["clusters.members"] = result.Clusters.Sum(cluster => cluster.Members.Count);
        state.Similarity = result;
        state.Counts["clusters.families"] = result.Clusters.Count(cluster => cluster.Members.Count > 1);
        state.Counts["clusters.lowCohesion"] = result.Clusters.Count(cluster => cluster.LowCohesion);
        state.Counts["pairs.reported"] = result.Pairs.Count;
        state.StagesRun.Add("similarity");
        logger.LogInformation("Similarity: {Families} families of 2+, {Clusters} clusters in total, {Pairs} pairs above the floor",
            state.Counts["clusters.families"], result.Clusters.Count, result.Pairs.Count);
        return result;
    }
}
