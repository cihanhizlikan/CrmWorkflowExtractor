using Crm.Extract.Inventory;
using Crm.Ir.Model;
using Xunit;

namespace Crm.Tests.Extract;

/// <summary>
/// <c>Crm.Ir</c> cannot reference <c>Crm.Extract</c> (structure.md), so the labels the reports print live in two
/// places: the §3.1 option tables and <see cref="ProcessLabels"/>, which code branches on. If they ever drift, a
/// usage verdict or a priority band silently stops matching — this is the test that will not let that happen.
/// </summary>
public sealed class OptionLabelTests
{
    [Theory]
    [InlineData("category", 0, ProcessLabels.CategoryWorkflow)]
    [InlineData("category", 1, ProcessLabels.CategoryDialog)]
    [InlineData("category", 2, ProcessLabels.CategoryBusinessRule)]
    [InlineData("category", 3, ProcessLabels.CategoryAction)]
    [InlineData("category", 4, ProcessLabels.CategoryBusinessProcessFlow)]
    [InlineData("mode", 0, ProcessLabels.ModeBackground)]
    [InlineData("mode", 1, ProcessLabels.ModeRealTime)]
    [InlineData("statecode", 0, ProcessLabels.StateDraft)]
    [InlineData("statecode", 1, ProcessLabels.StateActivated)]
    public void The_Label_Code_Branches_On_Is_The_Label_The_Option_Table_Produces(string column, int raw, string expected)
    {
        Assert.Equal(expected, WorkflowOptionSets.Describe(column, raw).Label);
    }

    /// <summary>The output is Turkish; a label left in English would be a translation someone missed.</summary>
    [Fact]
    public void No_Option_Label_Was_Left_In_English()
    {
        List<string> labels = [.. WorkflowOptionSets.ByColumn.Values.SelectMany(column => column.Values).Distinct(StringComparer.Ordinal)];

        Assert.DoesNotContain(labels, label => label is "Draft" or "Activated" or "Workflow" or "Dialog" or "Business Rule"
            or "Action" or "Business Process Flow" or "Background" or "Real-time" or "Definition" or "Activation"
            or "Template" or "User" or "Organization" or "Owner" or "Calling User" or "Pre-operation" or "Post-operation");
    }
}
