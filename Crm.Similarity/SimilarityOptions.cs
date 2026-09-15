namespace Crm.Similarity;

/// <summary>
/// §7 weights and thresholds, bound from the <c>Similarity</c> configuration section. Every score is a weighted mean
/// of explainable parts, and the architects are expected to tune these after the first output.
/// </summary>
public sealed class SimilarityOptions
{
    public const string SectionName = "Similarity";

    /// <summary>Combined = StructuralWeight × structural + LexicalWeight × lexical (normalized by their sum).</summary>
    public double StructuralWeight { get; set; } = 0.6;

    public double LexicalWeight { get; set; } = 0.4;

    /// <summary>Structural = PathWeight × path-set Jaccard + ShingleWeight × 2/3-gram Jaccard.</summary>
    public double PathWeight { get; set; } = 0.6;

    public double ShingleWeight { get; set; } = 0.4;

    /// <summary>Lexical = PrefixWeight × common token prefix + TokenJaccardWeight × token-set Jaccard + JaroWinklerWeight × Jaro–Winkler.</summary>
    public double PrefixWeight { get; set; } = 0.4;

    public double TokenJaccardWeight { get; set; } = 0.3;

    public double JaroWinklerWeight { get; set; } = 0.3;

    /// <summary>An edge joins two workflows into a family when their combined score reaches this.</summary>
    public double ClusterThreshold { get; set; } = 0.75;

    /// <summary>Pairs at or above this go to pairs.csv — the audit trail behind every family.</summary>
    public double PairFloor { get; set; } = 0.4;

    /// <summary>A family whose weakest internal pair is below ClusterThreshold − CohesionMargin is flagged low-cohesion (a chain, not a family).</summary>
    public double CohesionMargin { get; set; } = 0.2;

    /// <summary>Name tokens used by fewer workflows than this are treated as product codes or sequence numbers and ignored by token Jaccard.</summary>
    public int RareTokenMinWorkflows { get; set; } = 2;

    /// <summary>Execution paths per workflow are capped, deterministically, so a workflow with many sequential conditions cannot explode.</summary>
    public int MaxPathsPerWorkflow { get; set; } = 512;
}
