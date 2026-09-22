using System.Globalization;
using System.Xml.Linq;
using Crm.Bpmn;
using Crm.Consolidation;
using Crm.Ir.Model;
using Crm.Similarity;
using Crm.Tests.Bpmn;
using Xunit;

namespace Crm.Tests.Consolidation;

public sealed class WorkflowCombinerTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    [Fact]
    public void Identical_Shapes_Become_One_Step_Keeping_Every_Value_And_Who_Set_It()
    {
        Dictionary<Guid, WorkflowIr> documents = Documents(
            (A, "Poliçe İptal Kasko", [Update("new_status", "100000003")]),
            (B, "Poliçe İptal Trafik", [Update("new_status", "100000007")]));

        CombineOutcome outcome = WorkflowCombiner.Combine(Family(documents), documents);

        WorkflowIr combined = Assert.IsType<WorkflowIr>(outcome.Combined);
        StepNode update = Assert.Single(combined.Steps);
        Assert.Equal(StepKind.UpdateRecord, update.Kind);
        Assert.Equal([new StepSource(A, "0"), new StepSource(B, "0")], update.Sources);
        FieldWrite field = Assert.Single(update.Fields);
        Assert.Equal(["100000003", "100000007"], field.Values.Select(value => value.Raw));
        Assert.Equal([A], field.Values[0].Workflows);
        Assert.Equal([B], field.Values[1].Workflows);
        Assert.Empty(outcome.ReconciliationErrors);
    }

    [Fact]
    public void Where_Members_Diverge_A_Variant_Names_Exactly_Who_Takes_Each_Path()
    {
        Dictionary<Guid, WorkflowIr> documents = Documents(
            (A, "Kasko", [Update("new_status", "1"), Stop()]),
            (B, "Trafik", [Update("new_status", "2"), Create("task")]),
            (C, "Konut", [Update("new_status", "3"), Stop()]));

        CombineOutcome outcome = WorkflowCombiner.Combine(Family(documents), documents);

        WorkflowIr combined = outcome.Combined!;
        Assert.Equal(2, combined.Steps.Count);
        StepNode variant = combined.Steps[1];
        Assert.Equal(StepKind.Variant, variant.Kind);
        Assert.Equal(2, variant.Branches.Count);
        Branch stops = Assert.Single(variant.Branches, branch => branch.Members.Count == 2);
        Assert.Equal([A, C], stops.Members);
        Assert.Equal("Kasko, Konut", stops.Label);
        Assert.Equal([new StepSource(A, "1"), new StepSource(C, "1")], Assert.Single(stops.Steps).Sources);
        Assert.Equal([B], Assert.Single(variant.Branches, branch => branch.Members.Count == 1).Members);
        Assert.Empty(outcome.ReconciliationErrors);
    }

    [Fact]
    public void A_Member_That_Ends_Earlier_Gets_An_Explicit_Ends_Here_Branch()
    {
        Dictionary<Guid, WorkflowIr> documents = Documents(
            (A, "Kısa", [Update("new_status", "1")]),
            (B, "Uzun", [Update("new_status", "1"), Create("task")]));

        WorkflowIr combined = WorkflowCombiner.Combine(Family(documents), documents).Combined!;

        StepNode variant = combined.Steps[1];
        Branch ends = Assert.Single(variant.Branches, branch => branch.Steps.Count == 0);
        Assert.StartsWith("(ends here)", ends.Label, StringComparison.Ordinal);
        Assert.Equal([A], ends.Members);
    }

    [Fact]
    public void Matching_Conditions_Merge_And_Divergence_Inside_A_Branch_Stays_Inside_It()
    {
        Dictionary<Guid, WorkflowIr> documents = Documents(
            (A, "Kasko", [Condition(Update("new_status", "1"))]),
            (B, "Trafik", [Condition(Create("task"))]));

        CombineOutcome outcome = WorkflowCombiner.Combine(Family(documents), documents);

        StepNode condition = Assert.Single(outcome.Combined!.Steps);
        Assert.Equal(StepKind.Condition, condition.Kind);
        Assert.Equal([new StepSource(A, "0"), new StepSource(B, "0")], condition.Sources);
        Assert.Equal(StepKind.Variant, Assert.Single(condition.Branches[0].Steps).Kind);
        Assert.Empty(outcome.ReconciliationErrors);
    }

    /// <summary>The run-failing guard: a combined workflow that lost a member step must be reported.</summary>
    [Fact]
    public void Reconciliation_Reports_A_Member_Step_The_Combination_Lost()
    {
        Dictionary<Guid, WorkflowIr> documents = Documents(
            (A, "Kasko", [Update("new_status", "1"), Stop()]),
            (B, "Trafik", [Update("new_status", "2"), Stop()]));
        WorkflowIr combined = WorkflowCombiner.Combine(Family(documents), documents).Combined!;
        WorkflowIr lossy = combined with { Steps = [combined.Steps[0]] };

        IReadOnlyList<string> errors = WorkflowCombiner.Reconcile([documents[A], documents[B]], lossy);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, error => error.Contains($"{A:D}@1", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Low_Cohesion_Family_Is_Not_Combined()
    {
        Dictionary<Guid, WorkflowIr> documents = Documents((A, "A", [Stop()]), (B, "B", [Stop()]), (C, "C", [Stop()]));

        CombineOutcome outcome = WorkflowCombiner.Combine(Family(documents) with { LowCohesion = true }, documents);

        Assert.Null(outcome.Combined);
        Assert.StartsWith("low cohesion", outcome.SkippedBecause, StringComparison.Ordinal);
    }

    [Fact]
    public void Members_On_Different_Entities_Are_Not_Combined()
    {
        Dictionary<Guid, WorkflowIr> documents = Documents((A, "A", [Stop()]), (B, "B", [Stop()]));
        documents[B] = documents[B] with { Identity = documents[B].Identity with { PrimaryEntity = "new_claim" } };

        CombineOutcome outcome = WorkflowCombiner.Combine(Family(documents), documents);

        Assert.Null(outcome.Combined);
        Assert.StartsWith("members run on different primary entities", outcome.SkippedBecause, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Combined_Workflow_Emits_Valid_Bpmn_With_A_Variant_Gateway_And_Member_Provenance()
    {
        WorkflowIr first = BpmnEmissionTests.IrFor("condition-update-stop.xaml", A);
        WorkflowIr second = BpmnEmissionTests.IrFor("child-and-custom.xaml", B);
        Dictionary<Guid, WorkflowIr> documents = new() { [A] = first, [B] = second };
        CombineOutcome outcome = WorkflowCombiner.Combine(Family(documents), documents);

        XDocument xml = BpmnSerializer.ToXml(BpmnBuilder.Build(outcome.ClusterId, outcome.Combined!.Identity.Name, outcome.Combined, [new(A, ""), new(B, "")],
            new Dictionary<Guid, string> { [A] = "Birinci", [B] = "İkinci" }), "test");

        Assert.Empty(BpmnSchemaValidator.Validate(xml));
        Assert.Contains(xml.Descendants(BpmnSerializer.Model + "sequenceFlow"), flow => flow.Attribute("name")?.Value.StartsWith("Çeşitleme: ", StringComparison.Ordinal) == true);
        Assert.Contains(xml.Descendants(BpmnSerializer.Provenance + "source"), source => source.Attribute("workflowId")?.Value == B.ToString("D"));
        Assert.Empty(outcome.ReconciliationErrors);
    }

    private static WorkflowCluster Family(IReadOnlyDictionary<Guid, WorkflowIr> documents)
    {
        List<ClusterMember> members = [.. documents.Values.Select(document => new ClusterMember(document.Identity.WorkflowId, document.Identity.Name,
            document.Identity.PrimaryEntity, document.Identity.Category, document.Identity.State, 1))];
        Guid medoid = documents.Keys.Order().First();
        return new WorkflowCluster(SimilarityEngine.ClusterIdFor(medoid), medoid, members, 0.9, 1, false);
    }

    private static Dictionary<Guid, WorkflowIr> Documents(params (Guid Id, string Name, StepNode[] Steps)[] workflows)
    {
        return workflows.ToDictionary(workflow => workflow.Id, workflow => new WorkflowIr(
            new WorkflowIdentity(workflow.Id, workflow.Name, null, "İş Akışı", "Tanım", "new_policy", "Arka plan", "Kuruluş", "Etkin", false, false, null, null, null, 1, true),
            new WorkflowTrigger(true, false, [], null, null, null, "Owner", false),
            Paths(workflow.Id, workflow.Steps, ""),
            new WorkflowDependencies([], []), new DataTouched([], [], [], []), [], new IrProvenance("", "", "", "")));
    }

    /// <summary>Assigns real paths and sources, as the parser would.</summary>
    private static IReadOnlyList<StepNode> Paths(Guid id, IReadOnlyList<StepNode> steps, string parent)
    {
        List<StepNode> result = [];
        for (int index = 0; index < steps.Count; index++)
        {
            string path = parent.Length == 0 ? index.ToString(CultureInfo.InvariantCulture) : $"{parent}/{index}";
            StepNode step = steps[index];
            List<Branch> branches = [.. step.Branches.Select((branch, branchIndex) => branch with { Steps = Paths(id, branch.Steps, $"{path}/{branchIndex}") })];
            result.Add(step with { Path = path, Sources = [new StepSource(id, path)], Branches = branches });
        }
        return result;
    }

    private static StepNode Update(string field, string value)
    {
        return new StepNode("", StepKind.UpdateRecord, "Güncelle", "new_policy", [new FieldWrite(field, [new LiteralValue(value, null)])], [], null, [], "UpdateEntity", []);
    }

    private static StepNode Create(string entity)
    {
        return new StepNode("", StepKind.CreateRecord, "Oluştur", entity, [], [], null, [], "CreateEntity", []);
    }

    private static StepNode Stop()
    {
        return new StepNode("", StepKind.StopWorkflow, "Dur", null, [], [], "Succeeded", [], "TerminateWorkflow", []);
    }

    private static StepNode Condition(StepNode inside)
    {
        Predicate predicate = new("new_policy.new_status Equal 1", "new_policy", "new_status", "Equal", [new LiteralValue("1", null)]);
        return new StepNode("", StepKind.Condition, "Durum?", null, [], [new Branch("cond", predicate, [inside], []), new Branch("Otherwise", null, [], [])], null, [], "ConditionSequence", []);
    }
}
