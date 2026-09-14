using Crm.Extract.Preflight;
using Xunit;

namespace Crm.Tests.Extract;

public sealed class PreflightTests
{
    private static readonly string PrivilegesBody = "{\"value\":["
        + string.Join(",", PrivilegeCheck.Required.Select((privilege, index) => $"{{\"privilegeid\":\"{Id(index):D}\",\"name\":\"{privilege.Name}\"}}"))
        + "]}";

    [Fact]
    public void Organization_Depth_On_Every_Privilege_Is_Sufficient()
    {
        IReadOnlyList<PrivilegeFinding> findings = PrivilegeCheck.Evaluate(PrivilegesBody, Held("Global"));

        Assert.All(findings, finding => Assert.Equal(PrivilegeVerdict.Sufficient, finding.Verdict));
    }

    /// <summary>The §2.4 trap itself: user-level read on Process. Count reconciliation would pass; this must not.</summary>
    [Fact]
    public void User_Level_Read_Is_Insufficient()
    {
        IReadOnlyList<PrivilegeFinding> findings = PrivilegeCheck.Evaluate(PrivilegesBody, Held("Basic"));

        Assert.All(findings, finding => Assert.Equal(PrivilegeVerdict.Insufficient, finding.Verdict));
    }

    [Fact]
    public void The_Deepest_Grant_Across_Roles_Wins()
    {
        string held = "{\"RolePrivileges\":[" + string.Join(",", PrivilegeCheck.Required.SelectMany((_, index) => new[]
        {
            $"{{\"Depth\":\"Basic\",\"PrivilegeId\":\"{Id(index):D}\"}}",
            $"{{\"Depth\":3,\"PrivilegeId\":\"{Id(index):D}\"}}"
        })) + "]}";

        IReadOnlyList<PrivilegeFinding> findings = PrivilegeCheck.Evaluate(PrivilegesBody, held);

        Assert.All(findings, finding => Assert.Equal(PrivilegeVerdict.Sufficient, finding.Verdict));
    }

    [Fact]
    public void A_Privilege_Not_Held_Is_Missing_And_One_Not_Named_On_The_Server_Is_Unverifiable()
    {
        string onlyFirstFiveExist = "{\"value\":["
            + string.Join(",", PrivilegeCheck.Required.Take(5).Select((privilege, index) => $"{{\"privilegeid\":\"{Id(index):D}\",\"name\":\"{privilege.Name}\"}}"))
            + "]}";
        string heldExceptFirst = "{\"RolePrivileges\":["
            + string.Join(",", Enumerable.Range(1, 4).Select(index => $"{{\"Depth\":\"Global\",\"PrivilegeId\":\"{Id(index):D}\"}}"))
            + "]}";

        IReadOnlyList<PrivilegeFinding> findings = PrivilegeCheck.Evaluate(onlyFirstFiveExist, heldExceptFirst);

        Assert.Equal(PrivilegeVerdict.Missing, findings[0].Verdict);
        Assert.Equal(PrivilegeVerdict.Unverifiable, findings[5].Verdict);
    }

    [Fact]
    public void A_Column_The_Server_Does_Not_Have_Is_Excluded_And_Reported()
    {
        string attributes = AttributesBodyWithout(["businessprocesstype", "parentworkflowid"]);

        (IReadOnlyList<string> available, IReadOnlyList<string> missing) = WorkflowColumns.Split(attributes);

        Assert.Equal(["businessprocesstype", "_parentworkflowid_value"], missing);
        Assert.DoesNotContain("_parentworkflowid_value", available);
        Assert.Contains("_ownerid_value", available);
    }

    [Theory]
    [InlineData("_ownerid_value", "ownerid")]
    [InlineData("_parentworkflowid_value", "parentworkflowid")]
    [InlineData("name", "name")]
    public void A_Lookup_Column_Maps_To_Its_Attribute(string column, string attribute)
    {
        Assert.Equal(attribute, WorkflowColumns.AttributeNameOf(column));
    }

    private static string AttributesBodyWithout(IReadOnlyList<string> missing)
    {
        return "{\"value\":["
            + string.Join(",", WorkflowColumns.Inventory.Select(WorkflowColumns.AttributeNameOf).Where(name => !missing.Contains(name)).Select(name => $"{{\"LogicalName\":\"{name}\"}}"))
            + "]}";
    }

    private static string Held(string depth)
    {
        return "{\"RolePrivileges\":["
            + string.Join(",", PrivilegeCheck.Required.Select((_, index) => $"{{\"Depth\":\"{depth}\",\"PrivilegeId\":\"{Id(index):D}\"}}"))
            + "]}";
    }

    private static Guid Id(int index)
    {
        return new Guid(index + 1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]);
    }
}
