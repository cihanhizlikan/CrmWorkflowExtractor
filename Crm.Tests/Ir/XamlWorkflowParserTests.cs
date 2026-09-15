using Crm.Ir;
using Crm.Ir.Model;
using Crm.Ir.Parsing;
using Crm.Ir.Reports;
using Xunit;

namespace Crm.Tests.Ir;

public sealed class XamlWorkflowParserTests
{
    private static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static string Fixture(string name)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Xaml", name));
    }

    [Fact]
    public void A_Condition_With_An_Else_Branch_Becomes_Two_Branches_With_Resolved_Predicate()
    {
        OptionLabels labels = new();
        labels.Add("new_policy", "new_status", 100000003, "İptal Edildi");
        labels.Add("new_policy", "new_status", 100000007, "Askıda");

        ParseResult result = new XamlWorkflowParser(labels).Parse(Id, Fixture("condition-update-stop.xaml"));

        StepNode condition = Assert.Single(result.Steps);
        Assert.Equal(StepKind.Condition, condition.Kind);
        Assert.Equal("0", condition.Path);
        Assert.Equal(2, condition.Branches.Count);

        Predicate predicate = Assert.IsType<Predicate>(condition.Branches[0].Predicate);
        Assert.Equal("new_policy", predicate.Entity);
        Assert.Equal("new_status", predicate.Attribute);
        Assert.Equal("Equal", predicate.Operator);
        LiteralValue value = Assert.Single(predicate.Values);
        Assert.Equal("100000003", value.Raw);
        Assert.Equal("İptal Edildi", value.Resolved);
        Assert.Equal("new_policy.new_status Equal İptal Edildi (100000003)", predicate.Text);

        StepNode update = Assert.Single(condition.Branches[0].Steps);
        Assert.Equal(StepKind.UpdateRecord, update.Kind);
        Assert.Equal("0/0/0", update.Path);
        Assert.Equal("Poliçeyi askıya al", update.DisplayName);
        Assert.Equal("new_policy", update.Entity);
        FieldWrite field = Assert.Single(update.Fields);
        Assert.Equal("new_status", field.Field);
        Assert.Equal(new LiteralValue("100000007", "Askıda"), Assert.Single(field.Values));

        Assert.Equal("Otherwise", condition.Branches[1].Label);
        StepNode stop = Assert.Single(condition.Branches[1].Steps);
        Assert.Equal(StepKind.StopWorkflow, stop.Kind);
        Assert.Equal("Canceled", stop.Detail);
    }

    [Fact]
    public void Data_Touched_Records_What_Is_Read_And_Written()
    {
        ParseResult result = new XamlWorkflowParser(OptionLabels.Empty).Parse(Id, Fixture("condition-update-stop.xaml"));

        Assert.Equal(["new_policy.new_status"], result.DataTouched.FieldsRead);
        Assert.Equal(["new_policy.new_status"], result.DataTouched.FieldsWritten);
        Assert.Equal(["new_policy"], result.DataTouched.EntitiesWritten);
    }

    [Fact]
    public void A_Child_Workflow_And_A_Partner_Activity_Are_Captured_With_Their_Arguments()
    {
        ParseResult result = new XamlWorkflowParser(OptionLabels.Empty).Parse(Id, Fixture("child-and-custom.xaml"));

        Assert.Equal(2, result.Steps.Count);
        StepNode child = result.Steps[0];
        Assert.Equal(StepKind.StartChildWorkflow, child.Kind);
        Assert.Equal("7a0e1f52-9d4b-4c55-8e1c-3b6f2a9d0c11", child.Detail);
        Assert.Equal([Guid.Parse("7a0e1f52-9d4b-4c55-8e1c-3b6f2a9d0c11")], result.Dependencies.ChildWorkflowCalls);

        StepNode custom = result.Steps[1];
        Assert.Equal(StepKind.CustomActivity, custom.Kind);
        Assert.Equal("Partner.Crm.Activities.NotifyPolicyService, Partner.Crm.Activities", custom.Detail);
        Assert.Contains(new NamedArgument("ServiceUrl", "https://servis.ornek.local/policy/notify"), custom.Arguments);
        Assert.Equal(["Partner.Crm.Activities.NotifyPolicyService, Partner.Crm.Activities"], result.Dependencies.CustomActivities);
    }

    [Fact]
    public void A_Wait_With_A_Timeout_Becomes_A_Wait_Condition_With_A_Timeout_Branch()
    {
        ParseResult result = new XamlWorkflowParser(OptionLabels.Empty).Parse(Id, Fixture("wait-timeout.xaml"));

        StepNode wait = Assert.Single(result.Steps);
        Assert.Equal(StepKind.WaitCondition, wait.Kind);
        Assert.Equal(2, wait.Branches.Count);
        Assert.Equal("new_policy.new_paid Equal True", wait.Branches[0].Label);
        Assert.Equal(StepKind.Timeout, Assert.Single(wait.Branches[1].Steps).Kind);
    }

    /// <summary>§4.4: nothing is dropped silently. An unknown activity becomes an Unmapped step; an unknown element inside a known step is counted and warned.</summary>
    [Fact]
    public void Unknown_Constructs_Are_Recorded_Never_Dropped()
    {
        ParseResult result = new XamlWorkflowParser(OptionLabels.Empty).Parse(Id, Fixture("unknown-construct.xaml"));

        Assert.Equal(StepKind.Unmapped, result.Steps[0].Kind);
        Assert.Equal("SomethingNewer", result.Steps[0].Construct);
        Assert.Equal(StepKind.CreateRecord, result.Steps[1].Kind);
        Assert.Contains(result.Coverage, observation => observation.Construct == "SomethingNewer" && observation.Status == CoverageStatus.Unmapped);
        Assert.Contains(result.Coverage, observation => observation.Construct == "Mystery" && observation.Status == CoverageStatus.Unmapped && observation.Path == "1");
        Assert.Contains(result.Warnings, warning => warning.Message.Contains("Mystery", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("condition-update-stop.xaml")]
    [InlineData("child-and-custom.xaml")]
    [InlineData("wait-timeout.xaml")]
    [InlineData("unknown-construct.xaml")]
    public void Every_Element_Under_The_Workflow_Is_Accounted_For(string fixture)
    {
        string xaml = Fixture(fixture);
        int elements = System.Xml.Linq.XDocument.Parse(xaml).Descendants().SkipWhile(element => element.Name.LocalName != "Workflow").Skip(1).Count();

        ParseResult result = new XamlWorkflowParser(OptionLabels.Empty).Parse(Id, xaml);

        Assert.Equal(elements, result.Coverage.Count);
    }

    [Fact]
    public void A_Partner_Url_And_Password_Are_Found_And_Their_Values_Are_Not_In_The_Report()
    {
        ParseResult result = new XamlWorkflowParser(OptionLabels.Empty).Parse(Id, Fixture("child-and-custom.xaml"));

        IReadOnlyList<SensitiveFinding> findings = SensitiveLiteralScanner.Scan(Id, "Poliçe", "raw/xaml/x.xaml", result.Literals);
        string report = SensitiveLiteralScanner.Markdown(findings);

        Assert.Contains(findings, finding => finding.Category == "hardcoded URL");
        Assert.Contains(findings, finding => finding.Category == "password or secret");
        Assert.DoesNotContain("NotARealSecret1", report, StringComparison.Ordinal);
        Assert.DoesNotContain("servis.ornek.local", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_Xaml_Throws_So_The_Stage_Can_Count_It()
    {
        Assert.Throws<System.Xml.XmlException>(() => new XamlWorkflowParser(OptionLabels.Empty).Parse(Id, "<Activity><unclosed></Activity>"));
    }
}
