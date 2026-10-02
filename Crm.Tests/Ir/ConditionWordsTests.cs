using Crm.Ir;
using Crm.Ir.Model;
using Crm.Ir.Parsing;
using Xunit;

namespace Crm.Tests.Ir;

/// <summary>
/// CRM's operators are .NET enum names, and a diagram reading <c>lead.leadid NotNull</c> left a reader asking
/// whether it was a null check at all. The name stays on the predicate, where it is a comparison key; the words
/// change only where a person reads them.
/// </summary>
public sealed class ConditionWordsTests
{
    [Theory]
    [InlineData("Equal", "=")]
    [InlineData("NotEqual", "≠")]
    [InlineData("NotNull", "dolu")]
    [InlineData("Null", "boş")]
    [InlineData("In", "şunlardan biri:")]
    [InlineData("GreaterEqual", "≥")]
    [InlineData("OlderThanXMonths", "şu kadar aydan eski:")]
    public void An_Operator_Is_Read_In_Words(string crm, string expected)
    {
        Assert.Equal(expected, ConditionWords.Of(crm));
    }

    /// <summary>
    /// CRM can add operators, and a guessed translation would read as fact. One with no entry keeps its own name,
    /// which reads as what it is: a word nobody has translated.
    /// </summary>
    [Fact]
    public void An_Operator_Nobody_Translated_Keeps_Its_Own_Name()
    {
        Assert.Equal("ContainsSomeFutureThing", ConditionWords.Of("ContainsSomeFutureThing"));
        Assert.Equal("", ConditionWords.Of(null));
    }

    [Theory]
    [InlineData("And", "VE")]
    [InlineData("Or", "VEYA")]
    public void Two_Conditions_Are_Joined_In_Words(string crm, string expected)
    {
        Assert.Equal(expected, ConditionWords.Join(crm));
    }

    /// <summary>
    /// The operator's NAME is a comparison key — two workflows testing the same field the same way have to fold
    /// together whatever the wording is — so it stays on the predicate untranslated.
    /// </summary>
    [Fact]
    public void The_Comparison_Key_Keeps_Crms_Own_Name()
    {
        OptionLabels labels = new();
        labels.Add("new_policy", "new_status", 100000003, "İptal Edildi");

        ParseResult result = new XamlWorkflowParser(labels).Parse(
            new Guid("00000000-0000-0000-0000-000000000001"), XamlWorkflowParserTests.Fixture("condition-update-stop.xaml"));
        Predicate predicate = Assert.IsType<Predicate>(Assert.Single(result.Steps).Branches[0].Predicate);

        Assert.Equal("Equal", predicate.Operator);
        Assert.Equal("new_policy.new_status", predicate.Subject);
        Assert.Equal("new_policy.new_status = İptal Edildi (100000003)", predicate.Text);
    }
}
