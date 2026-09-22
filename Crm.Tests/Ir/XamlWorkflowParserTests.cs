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

    /// <summary>The helper constructs the first production run reported as unmapped in 595 workflows (2026-09-22).</summary>
    [Fact]
    public void Type_Conversions_Typed_Literals_And_Related_Record_Loads_Are_Support_Not_Steps()
    {
        ParseResult result = new XamlWorkflowParser(OptionLabels.Empty).Parse(Id, Fixture("production-helpers.xaml"));

        Assert.DoesNotContain(result.Coverage, observation => observation.Status == CoverageStatus.Unmapped);
        Assert.Equal([StepKind.UpdateRecord, StepKind.CreateRecord, StepKind.Sequence, StepKind.ChangeStatus], result.Steps.Select(step => step.Kind));
        Assert.Equal(StepKind.Timeout, Assert.Single(result.Steps[2].Branches[0].Steps).Kind);
        Assert.Contains("WaitStep3_1", result.Steps[2].Branches[0].Steps[0].Detail, StringComparison.Ordinal);
        Assert.Contains("systemuser", result.DataTouched.EntitiesRead);
    }

    [Fact]
    public void A_Value_Passed_Through_A_Type_Conversion_Keeps_Its_Literal()
    {
        ParseResult result = new XamlWorkflowParser(OptionLabels.Empty).Parse(Id, Fixture("production-helpers.xaml"));

        FieldWrite status = Assert.Single(result.Steps[0].Fields);
        Assert.Equal("new_status", status.Field);
        Assert.Equal("100000007", Assert.Single(status.Values).Raw);
    }

    [Fact]
    public void An_If_That_Does_More_Than_Load_A_Related_Record_Stays_Unmapped()
    {
        string xaml = Fixture("production-helpers.xaml").Replace(
            "<mxswa:RetrieveEntity ",
            "<mxswa:UpdateEntity DisplayName=\"Hidden\" Entity=\"[x]\" EntityName=\"new_policy\" /><mxswa:RetrieveEntity ",
            StringComparison.Ordinal);

        ParseResult result = new XamlWorkflowParser(OptionLabels.Empty).Parse(Id, xaml);

        Assert.Contains(result.Coverage, observation => observation.Construct == "If" && observation.Status == CoverageStatus.Unmapped);
    }

    [Fact]
    public void Business_Rule_Actions_Become_Form_Actions_With_Their_Arguments()
    {
        ParseResult result = new XamlWorkflowParser(OptionLabels.Empty).Parse(Id, Fixture("business-rule.xaml"));

        Assert.DoesNotContain(result.Coverage, observation => observation.Status == CoverageStatus.Unmapped);
        StepNode condition = Assert.Single(result.Steps);
        Assert.Equal(StepKind.Condition, condition.Kind);
        Assert.Equal(["Show/hide field", "Set required level"], condition.Branches[0].Steps.Select(step => step.Detail));
        Assert.All(condition.Branches[0].Steps, step => Assert.Equal(StepKind.FormAction, step.Kind));
        Assert.Contains(condition.Branches[0].Steps[0].Arguments, argument => argument is { Name: "ControlId", Value: "new_reason" });
        StepNode otherwise = Assert.Single(condition.Branches[1].Steps);
        StepNode message = otherwise.Branches[0].Steps[0];
        Assert.Equal("Show error message", message.Detail);
        Assert.Contains(message.Arguments, argument => argument.Name == "StepLabels" && argument.Value.Contains("Askıya alma nedeni girilmelidir.", StringComparison.Ordinal));
        Assert.Equal("Lock/unlock field", otherwise.Branches[0].Steps[1].Detail);
    }

    [Fact]
    public void A_Dialog_Becomes_A_Query_A_Page_Of_Prompts_And_A_Child_Dialog_Call()
    {
        ParseResult result = new XamlWorkflowParser(OptionLabels.Empty).Parse(Id, Fixture("dialog.xaml"));

        Assert.DoesNotContain(result.Coverage, observation => observation.Status == CoverageStatus.Unmapped);
        Assert.Equal([StepKind.Sequence, StepKind.UserInteraction, StepKind.StartChildWorkflow], result.Steps.Select(step => step.Kind));
        Assert.Equal(StepKind.DataQuery, Assert.Single(result.Steps[0].Branches[0].Steps).Kind);
        Assert.Contains("new_policy", result.DataTouched.EntitiesRead);
        StepNode page = result.Steps[1];
        Assert.Equal("Müşteri onayı", page.DisplayName);
        Assert.Contains(page.Arguments, argument => argument is { Name: "Prompt1.PromptText", Value: "Müşteri iptali onaylıyor mu?" });
        Assert.Contains(page.Arguments, argument => argument is { Name: "Prompt2.ResponseType", Value: "Text" });
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", result.Steps[2].Detail);
        Assert.Contains(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), result.Dependencies.ChildWorkflowCalls);
    }

    [Theory]
    [InlineData("business-rule.xaml")]
    [InlineData("dialog.xaml")]
    [InlineData("production-helpers.xaml")]
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
