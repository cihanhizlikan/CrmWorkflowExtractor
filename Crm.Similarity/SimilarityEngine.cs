using Crm.Ir.Model;
using Crm.Ir.Text;

namespace Crm.Similarity;

/// <summary>What the engine keeps per workflow: identity, and the precomputed sets every pair compares.</summary>
public sealed record WorkflowProfile(
    WorkflowIdentity Identity,
    IReadOnlyList<string> NameTokens,
    string FoldedName,
    IReadOnlySet<string> Paths,
    IReadOnlySet<string> Shingles);

/// <summary>One pair's scores, components always beside the combination (§7.3).</summary>
public sealed record PairScore(Guid Left, Guid Right, double Structural, double Lexical, double Combined, double PathJaccard, double ShingleJaccard, double Prefix, double TokenJaccard, double JaroWinkler);

public sealed record ClusterMember(Guid WorkflowId, string Name, string? PrimaryEntity, string Category, string State, double ScoreToMedoid);

/// <summary>A proposed family. Never a decision: the medoid is a suggested starting point, the cohesion flag says when to split first.</summary>
public sealed record WorkflowCluster(
    string ClusterId,
    Guid Medoid,
    IReadOnlyList<ClusterMember> Members,
    double MinimumInternalScore,
    int ChainDepth,
    bool LowCohesion);

public sealed record SimilarityResult(IReadOnlyList<WorkflowCluster> Clusters, IReadOnlyList<PairScore> Pairs);

/// <summary>
/// §7: deterministic, explainable similarity. Every pair of workflows is scored (≈45,000 pairs at 300 workflows —
/// written plainly, as the handout asks), edges above the threshold are joined by union-find, and each resulting
/// family is measured for cohesion so a chain A–B–C with A and C unrelated is flagged rather than trusted.
/// </summary>
public sealed class SimilarityEngine(SimilarityOptions options)
{
    public SimilarityResult Run(IReadOnlyList<WorkflowIr> documents)
    {
        List<WorkflowProfile> profiles = [.. documents.OrderBy(document => document.Identity.WorkflowId).Select(Profile)];
        HashSet<string> commonTokens = CommonTokens(profiles);

        Dictionary<(int, int), PairScore> scores = [];
        List<PairScore> reported = [];
        UnionFind families = new(profiles.Count);
        for (int i = 0; i < profiles.Count; i++)
        {
            for (int j = i + 1; j < profiles.Count; j++)
            {
                PairScore score = Score(profiles[i], profiles[j], commonTokens);
                scores[(i, j)] = score;
                if (score.Combined >= options.PairFloor)
                {
                    reported.Add(score);
                }
                if (score.Combined >= options.ClusterThreshold)
                {
                    families.Union(i, j);
                }
            }
        }

        List<WorkflowCluster> clusters = [.. families.Groups()
            .Select(group => Cluster(group, profiles, scores))
            .OrderByDescending(cluster => cluster.Members.Count)
            .ThenBy(cluster => cluster.ClusterId, StringComparer.Ordinal)];
        return new SimilarityResult(clusters, [.. reported.OrderByDescending(pair => pair.Combined).ThenBy(pair => pair.Left).ThenBy(pair => pair.Right)]);
    }

    public PairScore Score(WorkflowProfile left, WorkflowProfile right, IReadOnlySet<string> commonTokens)
    {
        double path = NameSimilarity.Jaccard(left.Paths, right.Paths);
        double shingle = NameSimilarity.Jaccard(left.Shingles, right.Shingles);
        double structural = Weighted((options.PathWeight, path), (options.ShingleWeight, shingle));

        double prefix = NameSimilarity.CommonPrefix(left.NameTokens, right.NameTokens);
        HashSet<string> leftTokens = new(left.NameTokens.Where(commonTokens.Contains), StringComparer.Ordinal);
        HashSet<string> rightTokens = new(right.NameTokens.Where(commonTokens.Contains), StringComparer.Ordinal);
        double tokens = NameSimilarity.Jaccard(leftTokens, rightTokens);
        double jaroWinkler = NameSimilarity.JaroWinkler(left.FoldedName, right.FoldedName);
        double lexical = Weighted((options.PrefixWeight, prefix), (options.TokenJaccardWeight, tokens), (options.JaroWinklerWeight, jaroWinkler));

        double combined = Weighted((options.StructuralWeight, structural), (options.LexicalWeight, lexical));
        (Guid first, Guid second) = left.Identity.WorkflowId.CompareTo(right.Identity.WorkflowId) <= 0
            ? (left.Identity.WorkflowId, right.Identity.WorkflowId)
            : (right.Identity.WorkflowId, left.Identity.WorkflowId);
        return new PairScore(first, second, Round(structural), Round(lexical), Round(combined), Round(path), Round(shingle), Round(prefix), Round(tokens), Round(jaroWinkler));
    }

    public WorkflowProfile Profile(WorkflowIr document)
    {
        IReadOnlyList<string> linear = StepSignature.Linear(document.Steps);
        return new WorkflowProfile(
            document.Identity,
            NameSimilarity.Tokens(document.Identity.Name),
            TurkishFold.Fold(document.Identity.Name),
            new HashSet<string>(StepSignature.Paths(document.Steps, options.MaxPathsPerWorkflow), StringComparer.Ordinal),
            StepSignature.Shingles(linear));
    }

    /// <summary>Tokens used by at least <see cref="SimilarityOptions.RareTokenMinWorkflows"/> workflows; the rest are product codes and sequence numbers. Pure numbers never count.</summary>
    private HashSet<string> CommonTokens(IReadOnlyList<WorkflowProfile> profiles)
    {
        return [.. profiles
            .SelectMany(profile => profile.NameTokens.Distinct(StringComparer.Ordinal))
            .Where(token => !token.All(char.IsDigit))
            .GroupBy(token => token, StringComparer.Ordinal)
            .Where(group => group.Count() >= options.RareTokenMinWorkflows)
            .Select(group => group.Key)];
    }

    private WorkflowCluster Cluster(IReadOnlyList<int> group, IReadOnlyList<WorkflowProfile> profiles, IReadOnlyDictionary<(int, int), PairScore> scores)
    {
        double ScoreOf(int a, int b)
        {
            return a == b ? 1 : scores[(Math.Min(a, b), Math.Max(a, b))].Combined;
        }

        int medoid = group
            .OrderByDescending(candidate => group.Count == 1 ? 1 : group.Where(other => other != candidate).Average(other => ScoreOf(candidate, other)))
            .ThenBy(candidate => profiles[candidate].Identity.WorkflowId)
            .First();
        double minimum = 1;
        foreach (int a in group)
        {
            foreach (int b in group.Where(b => b > a))
            {
                minimum = Math.Min(minimum, ScoreOf(a, b));
            }
        }

        List<ClusterMember> members = [.. group
            .Select(index => new ClusterMember(profiles[index].Identity.WorkflowId, profiles[index].Identity.Name, profiles[index].Identity.PrimaryEntity,
                profiles[index].Identity.Category, profiles[index].Identity.State, Round(ScoreOf(index, medoid))))
            .OrderByDescending(member => member.ScoreToMedoid)
            .ThenBy(member => member.WorkflowId)];
        bool lowCohesion = group.Count > 2 && minimum < options.ClusterThreshold - options.CohesionMargin;
        return new WorkflowCluster(ClusterIdFor(profiles[medoid].Identity.WorkflowId), profiles[medoid].Identity.WorkflowId, members,
            Round(minimum), ChainDepth(group, scores), lowCohesion);
    }

    /// <summary>Stable across runs: derived from the medoid's workflow id, never from an ordinal (§7.3).</summary>
    public static string ClusterIdFor(Guid medoid)
    {
        return "cl_" + medoid.ToString("N");
    }

    /// <summary>The diameter of the family's threshold graph: how many hops the longest chain of pairwise matches needs.</summary>
    private int ChainDepth(IReadOnlyList<int> group, IReadOnlyDictionary<(int, int), PairScore> scores)
    {
        int diameter = 0;
        foreach (int start in group)
        {
            Dictionary<int, int> distance = new() { [start] = 0 };
            Queue<int> queue = new([start]);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (int next in group.Where(candidate => !distance.ContainsKey(candidate)
                    && scores[(Math.Min(current, candidate), Math.Max(current, candidate))].Combined >= options.ClusterThreshold))
                {
                    distance[next] = distance[current] + 1;
                    queue.Enqueue(next);
                }
            }
            diameter = Math.Max(diameter, distance.Values.Max());
        }
        return diameter;
    }

    private static double Weighted(params (double Weight, double Value)[] parts)
    {
        double total = parts.Sum(part => part.Weight);
        return total <= 0 ? 0 : parts.Sum(part => part.Weight * part.Value) / total;
    }

    private static double Round(double value)
    {
        return Math.Round(value, 4, MidpointRounding.AwayFromZero);
    }

    private sealed class UnionFind(int size)
    {
        private readonly int[] _parent = [.. Enumerable.Range(0, size)];

        public void Union(int a, int b)
        {
            int rootA = Find(a);
            int rootB = Find(b);
            if (rootA != rootB)
            {
                _parent[Math.Max(rootA, rootB)] = Math.Min(rootA, rootB);
            }
        }

        public IEnumerable<IReadOnlyList<int>> Groups()
        {
            return Enumerable.Range(0, _parent.Length).GroupBy(Find).Select(Materialize);
        }

        private static IReadOnlyList<int> Materialize(IEnumerable<int> group)
        {
            return [.. group];
        }

        private int Find(int node)
        {
            while (_parent[node] != node)
            {
                _parent[node] = _parent[_parent[node]];
                node = _parent[node];
            }
            return node;
        }
    }
}
