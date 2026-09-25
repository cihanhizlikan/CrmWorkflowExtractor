using System.Text.Json;
using Crm.Extract.Security;
using Xunit;

namespace Crm.Tests.Extract;

/// <summary>
/// Who may start a process by hand. The trap this guards is the one that makes such a list dangerous rather than
/// merely wrong: a role that can only READ processes must never appear as a role that can RUN them.
/// </summary>
public sealed class RoleRetrieverTests
{
    private static readonly Guid Run = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Read = new("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Admin = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Operations = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Reader = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Team = new("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static IReadOnlyList<JsonElement> Rows(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateArray().Select(row => row.Clone())];
    }

    private static IReadOnlyList<RunRole> Build()
    {
        IReadOnlyList<JsonElement> grants = Rows($$"""
            [
              { "roleid": "{{Admin}}", "privilegeid": "{{Run}}", "privilegedepthmask": 8 },
              { "roleid": "{{Operations}}", "privilegeid": "{{Run}}", "privilegedepthmask": 1 },
              { "roleid": "{{Operations}}", "privilegeid": "{{Run}}", "privilegedepthmask": 2 },
              { "roleid": "{{Operations}}", "privilegeid": "{{Read}}", "privilegedepthmask": 4 },
              { "roleid": "{{Reader}}", "privilegeid": "{{Read}}", "privilegedepthmask": 8 }
            ]
            """);
        IReadOnlyList<JsonElement> roles = Rows($$"""
            [
              { "roleid": "{{Admin}}", "name": "Sistem Yöneticisi" },
              { "roleid": "{{Operations}}", "name": "Poliçe Operasyon",
                "_businessunitid_value@OData.Community.Display.V1.FormattedValue": "Operasyon" },
              { "roleid": "{{Reader}}", "name": "Sadece Okuyan" }
            ]
            """);
        IReadOnlyList<JsonElement> userRoles = Rows($$"""
            [
              { "roleid": "{{Admin}}", "systemuserid": "99999999-9999-9999-9999-999999999999" },
              { "roleid": "{{Operations}}", "systemuserid": "99999999-9999-9999-9999-999999999999" },
              { "roleid": "{{Operations}}", "systemuserid": "88888888-8888-8888-8888-888888888888" }
            ]
            """);
        IReadOnlyList<JsonElement> teamRoles = Rows($$"""[{ "roleid": "{{Operations}}", "teamid": "{{Team}}" }]""");
        return RoleRetriever.Build(grants, roles, userRoles, teamRoles, [new OwnerTeam(Team, "Poliçe Ekibi", 0)], Run, Read);
    }

    [Fact]
    public void A_Role_That_Can_Only_Read_Processes_Is_Not_Listed_As_Able_To_Run_Them()
    {
        IReadOnlyList<RunRole> roles = Build();

        Assert.DoesNotContain(roles, role => role.RoleId == Reader);
        Assert.Equal(2, roles.Count);
    }

    /// <summary>A role can hold one privilege through more than one grant; the deepest is the one that decides.</summary>
    [Fact]
    public void The_Deepest_Grant_Of_A_Privilege_Wins()
    {
        RunRole operations = Build().Single(role => role.RoleId == Operations);

        Assert.Equal(2, operations.RunDepthMask);
        Assert.Equal(4, operations.ProcessDepthMask);
    }

    /// <summary>
    /// Holders are counted, and teams named. The count is the answer to "which group"; the names of three thousand
    /// people are not, and carrying them out of the building is a cost with no matching use.
    /// </summary>
    [Fact]
    public void Holders_Are_Counted_And_Teams_Named()
    {
        RunRole operations = Build().Single(role => role.RoleId == Operations);

        Assert.Equal(2, operations.Users);
        Assert.Equal("Poliçe Ekibi", Assert.Single(operations.Teams));
        Assert.Equal("Operasyon", operations.BusinessUnit);
    }

    /// <summary>Busiest first: the role that would change the most people's day is the one to read first.</summary>
    [Fact]
    public void The_Role_With_The_Most_Holders_Comes_First()
    {
        Assert.Equal(Operations, Build()[0].RoleId);
    }

    /// <summary>The export's own shape, which the browser produces and <c>Run:ImportFile</c> reads back.</summary>
    [Fact]
    public void The_Export_Shape_Round_Trips()
    {
        using JsonDocument document = JsonDocument.Parse($$"""
            {
              "roles": [{ "roleId": "{{Operations}}", "name": "Poliçe Operasyon", "businessUnit": "Operasyon",
                          "runDepthMask": 2, "processDepthMask": 4, "users": 2, "teams": ["Poliçe Ekibi"] }],
              "teams": [{ "teamid": "{{Team}}", "name": "Poliçe Ekibi", "teamtype": 0 }],
              "note": null
            }
            """);

        RunAuthority authority = RoleRetriever.Parse(document.RootElement);

        RunRole role = Assert.Single(authority.Roles);
        Assert.Equal("Poliçe Operasyon", role.Name);
        Assert.Equal(2, role.Users);
        Assert.Equal("Poliçe Ekibi", Assert.Single(role.Teams));
        Assert.Equal(Team, Assert.Single(authority.Teams).TeamId);
        Assert.Null(authority.Note);
    }

    /// <summary>An intersect table may write its keys either way round; both are read.</summary>
    [Fact]
    public void A_Key_Written_As_A_Lookup_Is_Read_The_Same()
    {
        using JsonDocument document = JsonDocument.Parse($$"""{ "_roleid_value": "{{Admin}}" }""");

        Assert.Equal(Admin, RoleRetriever.Lookup(document.RootElement, "roleid"));
    }
}
