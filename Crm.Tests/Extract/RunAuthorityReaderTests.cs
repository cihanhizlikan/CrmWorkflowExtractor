using System.Text.Json;
using Crm.Extract.Security;
using Xunit;

namespace Crm.Tests.Extract;

/// <summary>
/// Who may start a process by hand, as the browser export reduced it. The reduction itself — above all the rule that
/// a role able only to READ processes is never listed as able to RUN them — happens in the browser, in
/// <c>tools/crm-browser-export.js</c>; this reads what it wrote.
/// </summary>
public sealed class RunAuthorityReaderTests
{
    private static readonly Guid Operations = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Team = new("cccccccc-cccc-cccc-cccc-cccccccccccc");

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

        RunAuthority authority = RunAuthorityReader.Parse(document.RootElement);

        RunRole role = Assert.Single(authority.Roles);
        Assert.Equal("Poliçe Operasyon", role.Name);
        Assert.Equal(2, role.Users);
        Assert.Equal("Poliçe Ekibi", Assert.Single(role.Teams));
        Assert.Equal(Team, Assert.Single(authority.Teams).TeamId);
        Assert.Null(authority.Note);
    }

    /// <summary>Why a piece is missing travels with it, so an empty page can say whether nobody holds the privilege or the read was refused.</summary>
    [Fact]
    public void A_Note_From_The_Export_Is_Kept()
    {
        using JsonDocument document = JsonDocument.Parse("""{ "roles": [], "teams": [], "note": "roleprivileges okunamadı" }""");

        RunAuthority authority = RunAuthorityReader.Parse(document.RootElement);

        Assert.Empty(authority.Roles);
        Assert.Equal("roleprivileges okunamadı", authority.Note);
    }
}
