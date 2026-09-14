using Crm.Extract.Inventory;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Extract;

public sealed class ReconciliationTests
{
    [Fact]
    public void Matching_Counts_With_Several_Owners_Pass_Without_Warnings()
    {
        ReconciliationResult result = InventoryReconciliation.Evaluate(4, [Definition(0, owner: 1), Activation(1, parent: 0, owner: 2), Definition(2, owner: 3), Activation(3, parent: 2, owner: 1)]);

        Assert.True(result.Passed);
        Assert.Empty(result.Warnings);
        Assert.Equal(2, result.Counts.Definitions);
        Assert.Equal(2, result.Counts.Activations);
        Assert.Equal(3, result.Counts.DistinctOwners);
    }

    [Fact]
    public void A_Count_That_Differs_From_The_Records_Retrieved_Fails()
    {
        ReconciliationResult result = InventoryReconciliation.Evaluate(5, [Definition(0, owner: 1), Definition(2, owner: 2)]);

        Assert.False(result.Passed);
        Assert.Contains(result.Failures, failure => failure.Contains("reported 5 workflows but 2 were retrieved", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_Records_Across_Pages_Fail_Even_When_The_Count_Matches()
    {
        ReconciliationResult result = InventoryReconciliation.Evaluate(2, [Definition(0, owner: 1), Definition(0, owner: 2)]);

        Assert.False(result.Passed);
        Assert.Contains(result.Failures, failure => failure.Contains("duplicates", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Count_At_The_Web_Api_Cap_Is_Not_Trusted()
    {
        List<WorkflowInventoryRecord> records = [.. Enumerable.Range(0, InventoryReconciliation.CountCap).Select(index => Definition(index * 2, owner: index))];

        ReconciliationResult result = InventoryReconciliation.Evaluate(InventoryReconciliation.CountCap, records);

        Assert.Contains(result.Failures, failure => failure.Contains("cap", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Single_Owner_Is_Warned_As_The_Privilege_Signature()
    {
        ReconciliationResult result = InventoryReconciliation.Evaluate(2, [Definition(0, owner: 7), Definition(2, owner: 7)]);

        Assert.True(result.Passed);
        Assert.Contains(result.Warnings, warning => warning.StartsWith("ONLY ONE DISTINCT OWNER", StringComparison.Ordinal));
    }

    [Fact]
    public void An_Option_Value_Outside_The_Table_Becomes_Unknown_And_A_Warning()
    {
        OptionValue value = WorkflowOptionSets.Describe("category", 6);
        ReconciliationResult result = InventoryReconciliation.Evaluate(1, [Definition(0, owner: 1) with
        {
            Category = value,
            ObservedOptions = [new ObservedOption("category", 6, value.Label, "Masaüstü Akışı")]
        }]);

        Assert.Equal("Unknown(6)", value.Label);
        Assert.False(value.IsKnown);
        Assert.Equal(6, value.Raw);
        Assert.Contains(result.Warnings, warning => warning.Contains("category=6", StringComparison.Ordinal) && warning.Contains("Masaüstü Akışı", StringComparison.Ordinal));
    }

    [Fact]
    public void An_Activation_Without_Its_Definition_Is_Warned()
    {
        ReconciliationResult result = InventoryReconciliation.Evaluate(2, [Definition(0, owner: 1), Activation(1, parent: 40, owner: 2)]);

        Assert.Contains(result.Warnings, warning => warning.Contains("activation record(s) point at no retrieved definition", StringComparison.Ordinal));
    }

    private static WorkflowInventoryRecord Definition(int index, int owner)
    {
        return Record(index, WorkflowOptionSets.TypeDefinition, owner, parent: null);
    }

    private static WorkflowInventoryRecord Activation(int index, int parent, int owner)
    {
        return Record(index, WorkflowOptionSets.TypeActivation, owner, parent);
    }

    private static WorkflowInventoryRecord Record(int index, int type, int owner, int? parent)
    {
        return new WorkflowInventoryRecord(
            FakeOrganization.WorkflowId(index),
            $"İş akışı {index}",
            "new_policy",
            WorkflowOptionSets.Describe("category", 0),
            WorkflowOptionSets.Describe("type", type),
            WorkflowOptionSets.Describe("mode", 0),
            WorkflowOptionSets.Describe("scope", 4),
            WorkflowOptionSets.Describe("statecode", 1),
            new Guid(owner, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]),
            parent is int parentIndex ? FakeOrganization.WorkflowId(parentIndex) : null,
            null,
            true,
            1,
            []);
    }
}
