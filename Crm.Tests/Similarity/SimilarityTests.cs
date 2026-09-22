using System.Globalization;
using Crm.Ir.Model;
using Crm.Similarity;
using Xunit;

namespace Crm.Tests.Similarity;

public sealed class SimilarityTests
{
    [Fact]
    public void Jaro_Winkler_Matches_The_Published_Reference_Values()
    {
        Assert.Equal(0.9611, Math.Round(NameSimilarity.JaroWinkler("MARTHA", "MARHTA"), 4));
        Assert.Equal(0.8400, Math.Round(NameSimilarity.JaroWinkler("DWAYNE", "DUANE"), 4));
        Assert.Equal(0.8133, Math.Round(NameSimilarity.JaroWinkler("DIXON", "DICKSONX"), 4));
    }

    [Fact]
    public void Name_Tokens_Split_On_Punctuation_Camel_Case_And_Digits_And_Fold_Turkish()
    {
        Assert.Equal(["police", "iptal", "sureci", "kasko", "2"], NameSimilarity.Tokens("POLİÇE İptal-Süreci_Kasko2"));
        Assert.Equal(["hasar", "bildirim", "onay"], NameSimilarity.Tokens("HasarBildirimOnay"));
    }

    /// <summary>§7.1: setting a field to 100000003 and to 100000007 must produce the same token.</summary>
    [Fact]
    public void Literal_Values_Do_Not_Affect_The_Structural_Score()
    {
        WorkflowIr left = Workflow("A", Update("new_policy", "new_status", "100000003"));
        WorkflowIr right = Workflow("B", Update("new_policy", "new_status", "100000007"));
        SimilarityEngine engine = new(new SimilarityOptions());

        PairScore score = engine.Score(engine.Profile(left), engine.Profile(right), new HashSet<string>());

        Assert.Equal(1.0, score.Structural);
    }

    [Fact]
    public void A_Different_Field_Is_A_Different_Structure()
    {
        WorkflowIr left = Workflow("A", Update("new_policy", "new_status", "1"));
        WorkflowIr right = Workflow("B", Update("new_policy", "new_owner", "1"));
        SimilarityEngine engine = new(new SimilarityOptions());

        Assert.True(engine.Score(engine.Profile(left), engine.Profile(right), new HashSet<string>()).Structural < 0.5);
    }

    /// <summary>The copy-paste-per-product pattern §7.2 is written for: same stem, product suffix, same steps.</summary>
    [Fact]
    public void Copies_Per_Product_Form_One_Family_And_An_Unrelated_Workflow_Stays_Apart()
    {
        List<WorkflowIr> documents =
        [
            Workflow("Poliçe İptal Süreci - Kasko", Update("new_policy", "new_status", "1"), Stop()),
            Workflow("POLİÇE İPTAL SÜRECİ - Trafik", Update("new_policy", "new_status", "2"), Stop()),
            Workflow("Poliçe İptal Süreci Konut", Update("new_policy", "new_status", "3"), Stop()),
            Workflow("Hasar Bildirim Onayı", Create("task"), Update("new_claim", "new_approved", "1"))
        ];

        SimilarityResult result = new SimilarityEngine(new SimilarityOptions()).Run(documents);

        WorkflowCluster family = result.Clusters[0];
        Assert.Equal(3, family.Members.Count);
        Assert.False(family.LowCohesion);
        Assert.Equal(SimilarityEngine.ClusterIdFor(family.Medoid), family.ClusterId);
        Assert.Single(result.Clusters[1].Members);
        Assert.Equal("Hasar Bildirim Onayı", result.Clusters[1].Members[0].Name);
    }

    [Fact]
    public void Running_Twice_Gives_The_Same_Clusters_And_Ids()
    {
        List<WorkflowIr> documents =
        [
            Workflow("Poliçe İptal Kasko", Update("new_policy", "new_status", "1")),
            Workflow("Poliçe İptal Trafik", Update("new_policy", "new_status", "2")),
            Workflow("Hasar Onay", Create("task"))
        ];
        SimilarityEngine engine = new(new SimilarityOptions());

        SimilarityResult first = engine.Run(documents);
        SimilarityResult second = engine.Run([.. documents.AsEnumerable().Reverse()]);

        Assert.Equal(first.Clusters.Select(cluster => cluster.ClusterId), second.Clusters.Select(cluster => cluster.ClusterId));
        Assert.Equal(first.Pairs, second.Pairs);
    }

    /// <summary>§7.3 chaining guard: A–B and B–C clear the threshold, A–C does not; the family must be flagged, not trusted.</summary>
    [Fact]
    public void A_Chain_Of_Matches_Is_Flagged_Low_Cohesion()
    {
        WorkflowIr a = Workflow("A", Update("e", "f1", "1"), Update("e", "f2", "1"), Update("e", "f3", "1"), Update("e", "f4", "1"));
        WorkflowIr b = Workflow("B", Update("e", "f3", "1"), Update("e", "f4", "1"), Update("e", "f5", "1"), Update("e", "f6", "1"));
        WorkflowIr c = Workflow("C", Update("e", "f5", "1"), Update("e", "f6", "1"), Update("e", "f7", "1"), Update("e", "f8", "1"));
        SimilarityOptions probe = new() { StructuralWeight = 1, LexicalWeight = 0, PathWeight = 0, ShingleWeight = 1 };
        SimilarityEngine scorer = new(probe);
        double ab = scorer.Score(scorer.Profile(a), scorer.Profile(b), new HashSet<string>()).Combined;
        double bc = scorer.Score(scorer.Profile(b), scorer.Profile(c), new HashSet<string>()).Combined;
        double ac = scorer.Score(scorer.Profile(a), scorer.Profile(c), new HashSet<string>()).Combined;
        Assert.True(ab > 0 && bc > 0 && ac < Math.Min(ab, bc), $"fixture scores ab={ab} bc={bc} ac={ac}");

        probe.ClusterThreshold = Math.Min(ab, bc);
        probe.CohesionMargin = (probe.ClusterThreshold - ac) / 2;
        SimilarityResult result = new SimilarityEngine(probe).Run([a, b, c]);

        WorkflowCluster chain = Assert.Single(result.Clusters);
        Assert.True(chain.LowCohesion);
        Assert.Equal(2, chain.ChainDepth);
        Assert.Equal(ac, chain.MinimumInternalScore);
    }

    private static WorkflowIr Workflow(string name, params StepNode[] steps)
    {
        Guid id = new(Math.Abs(name.GetHashCode(StringComparison.Ordinal)), 0, 0, [0, 0, 0, 0, 0, 0, 0, (byte)(name.Length % 255)]);
        return new WorkflowIr(
            new WorkflowIdentity(id, name, null, "İş Akışı", "Tanım", "new_policy", "Arka plan", "Kuruluş", "Etkin", false, false, null, null, null, 1, true),
            new WorkflowTrigger(true, false, [], null, null, null, "Owner", false),
            [.. steps.Select((step, index) => step with { Path = index.ToString(CultureInfo.InvariantCulture) })],
            new WorkflowDependencies([], []), new DataTouched([], [], [], []), [], new IrProvenance("", "", "", ""));
    }

    private static StepNode Update(string entity, string field, string value)
    {
        return new StepNode("0", StepKind.UpdateRecord, "", entity, [new FieldWrite(field, [new LiteralValue(value, null)])], [], null, [], "UpdateEntity", []);
    }

    private static StepNode Create(string entity)
    {
        return new StepNode("0", StepKind.CreateRecord, "", entity, [], [], null, [], "CreateEntity", []);
    }

    private static StepNode Stop()
    {
        return new StepNode("0", StepKind.StopWorkflow, "", null, [], [], "Succeeded", [], "TerminateWorkflow", []);
    }
}
