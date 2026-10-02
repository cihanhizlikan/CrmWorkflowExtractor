using Crm.Ir;
using Crm.Ir.Model;
using Crm.Ir.Parsing;
using Xunit;

namespace Crm.Tests.Ir;

/// <summary>
/// What a condition compares, named. Not every condition reads a field: the designer can compare whatever a custom
/// activity handed back, and then there is no entity and no attribute to print. The diagram used to print a bare
/// question mark there — "? = Aktif" — which names nothing and sends the reader to the XAML. The activity and
/// the output argument it wrote are in the definition, and they are what a reader needs.
/// </summary>
public sealed class ConditionSubjectTests
{
    private static readonly Guid Id = new("00000000-0000-0000-0000-000000000009");

    [Fact]
    public void A_Condition_On_An_Activity_Output_Is_Named_By_The_Activity_And_Its_Argument()
    {
        ParseResult result = new XamlWorkflowParser(new OptionLabels())
            .Parse(Id, XamlWorkflowParserTests.Fixture("condition-on-activity-output.xaml"));

        StepNode condition = Assert.Single(result.Steps);
        Predicate predicate = Assert.IsType<Predicate>(condition.Branches[0].Predicate);

        Assert.Equal("CheckPolicyStatus.Durum = Aktif", predicate.Text);
        Assert.DoesNotContain("?", predicate.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The value travels through <c>ConvertCrmXrmTypes</c> before the comparison. Naming the converter would be
    /// true and useless; the activity that produced the value is what the reader is after, so the conversion is
    /// followed back exactly as a literal already was.
    /// </summary>
    [Fact]
    public void A_Conversion_Does_Not_Hide_The_Activity_Behind_It()
    {
        ParseResult result = new XamlWorkflowParser(new OptionLabels())
            .Parse(Id, XamlWorkflowParserTests.Fixture("condition-on-activity-output.xaml"));

        Predicate predicate = Assert.IsType<Predicate>(Assert.Single(result.Steps).Branches[0].Predicate);

        Assert.DoesNotContain("ConvertCrmXrmTypes", predicate.Text, StringComparison.Ordinal);
    }
}
