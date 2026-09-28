using Crm.Ir;
using Crm.Ir.Model;
using Crm.Ir.Parsing;
using Xunit;

namespace Crm.Tests.Ir;

/// <summary>
/// Two shapes the TEST organisation's definitions actually use, both of which the parser read wrongly and neither
/// of which any earlier fixture had. Between them they turned a readable condition —
/// <c>phonecall.ps_activitytypeid NotEqual INBOUND - GELEN ARAMA</c> — into
/// <c>ConditionBranchStep12_1 NotEqual ps_activitytype</c>: a generated slot name compared against the name of a
/// table. Both halves of that were wrong, and both are here.
/// </summary>
public sealed class ConditionSubjectFormTests
{
    private static readonly Guid Id = new("00000000-0000-0000-0000-000000000011");

    private static Predicate Condition()
    {
        ParseResult result = new XamlWorkflowParser(new OptionLabels())
            .Parse(Id, XamlWorkflowParserTests.Fixture("condition-attribute-read-lookup.xaml"));
        return Assert.IsType<Predicate>(Assert.Single(result.Steps).Branches[0].Predicate);
    }

    /// <summary>
    /// <c>GetEntityProperty</c> writes the variable it reads into as an ATTRIBUTE here, not as a
    /// <c>GetEntityProperty.Value</c> property element. Reading only the element form left the condition with no
    /// field to name at all.
    /// </summary>
    [Fact]
    public void A_Field_Read_Written_As_An_Attribute_Still_Names_The_Field()
    {
        Predicate predicate = Condition();

        Assert.Equal("phonecall", predicate.Entity);
        Assert.Equal("ps_activitytypeid", predicate.Attribute);
    }

    /// <summary>
    /// A lookup's parameters put the TARGET TABLE first and the record second, so taking the first quoted string —
    /// right for every other type — reported the table's name as the value being compared against.
    /// </summary>
    [Fact]
    public void A_Lookup_Is_Compared_Against_The_Record_Not_The_Table()
    {
        Predicate predicate = Condition();

        Assert.Equal("INBOUND - GELEN ARAMA", Assert.Single(predicate.Values).Raw);
        Assert.Equal("phonecall.ps_activitytypeid NotEqual INBOUND - GELEN ARAMA", predicate.Text);
    }

    /// <summary>The types that were already right stay right: the first quoted string is the value for those.</summary>
    [Fact]
    public void An_Option_Set_Value_Is_Read_As_Before()
    {
        OptionLabels labels = new();
        labels.Add("new_policy", "new_status", 100000003, "İptal Edildi");

        ParseResult result = new XamlWorkflowParser(labels).Parse(Id, XamlWorkflowParserTests.Fixture("condition-update-stop.xaml"));
        Predicate predicate = Assert.IsType<Predicate>(Assert.Single(result.Steps).Branches[0].Predicate);

        Assert.Equal("new_policy.new_status Equal İptal Edildi (100000003)", predicate.Text);
    }
}
