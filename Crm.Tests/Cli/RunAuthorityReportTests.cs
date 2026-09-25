using Crm.Cli.Reports;
using Crm.Extract.Security;
using Crm.Ir.Model;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// The page that answers "who can run this". CRM grants the right estate-wide, so the per-workflow page carries
/// only what really is per workflow — whether a human can start it at all, and whose privileges its steps then
/// use. The wording matters more than usual here: a blank cell where the answer is "nobody, by design" reads as
/// missing data, and an analyst then goes to CRM to look for something that was never there.
/// </summary>
public sealed class RunAuthorityReportTests
{
    private static readonly Guid Team = new("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static WorkflowIr Workflow(string name, bool onDemand, string runAs, Guid? owner, string? ownerName)
    {
        WorkflowIdentity identity = new(Guid.NewGuid(), name, null, "İş Akışı", "Tanım", "new_policy", "Arka plan",
            "Kuruluş", "Etkin", false, onDemand, owner, null, null, 1, true)
        {
            OwnerName = ownerName,
            OwningBusinessUnit = "Genel Müdürlük"
        };
        return new WorkflowIr(identity, new WorkflowTrigger(false, false, [], null, null, null, runAs, onDemand), [],
            new WorkflowDependencies([], []), new DataTouched([], [], [], []), [], new IrProvenance("x", "y", "z", "1"));
    }

    private static IReadOnlyList<IReadOnlyList<object?>> Rows(Sheet sheet)
    {
        return sheet.Rows;
    }

    [Fact]
    public void A_Workflow_No_Human_Can_Start_Says_So_Instead_Of_Leaving_A_Blank()
    {
        Sheet sheet = RunAuthorityReport.Authority(
            [Workflow("Arka plan akışı", onDemand: false, "Sahip", null, null)], RunAuthority.Empty, new Dictionary<Guid, string>());

        object? cell = Rows(sheet)[0][sheet.Headers.ToList().IndexOf("elle_baslatilabilir")];
        Assert.Equal("hayır — yalnızca CRM tetikler", cell);
    }

    [Fact]
    public void An_On_Demand_Workflow_Carries_The_Identity_Its_Steps_Run_With()
    {
        Sheet sheet = RunAuthorityReport.Authority(
            [Workflow("Elle başlatılan", onDemand: true, "Çağıran Kullanıcı", Guid.NewGuid(), "Ayşe Yılmaz")],
            RunAuthority.Empty, new Dictionary<Guid, string>());

        List<string> headers = [.. sheet.Headers];
        IReadOnlyList<object?> row = Rows(sheet)[0];
        Assert.Equal("evet", row[headers.IndexOf("elle_baslatilabilir")]);
        Assert.Equal("Çağıran Kullanıcı", row[headers.IndexOf("calisma_kimligi")]);
        Assert.Equal("Ayşe Yılmaz", row[headers.IndexOf("sahip")]);
    }

    /// <summary>
    /// The owner lookup is polymorphic and the export carries only a name, so the kind is settled by asking whether
    /// that id is one of the teams the server returned — not by guessing from the name.
    /// </summary>
    [Fact]
    public void A_Team_Owner_Is_Told_Apart_From_A_User_Owner()
    {
        RunAuthority authority = new([], [new OwnerTeam(Team, "Poliçe Ekibi", 0)], null);
        Sheet sheet = RunAuthorityReport.Authority(
            [Workflow("Ekip sahipli", onDemand: true, "Sahip", Team, "Poliçe Ekibi"),
             Workflow("Kişi sahipli", onDemand: true, "Sahip", Guid.NewGuid(), "Ayşe Yılmaz")],
            authority, new Dictionary<Guid, string>());

        int kind = sheet.Headers.ToList().IndexOf("sahip_turu");
        Assert.Equal("ekip", Rows(sheet)[0][kind]);
        Assert.Equal("kullanıcı", Rows(sheet)[1][kind]);
    }

    /// <summary>CRM's depth mask, in the words CRM itself uses. A mask of 0 is "not held", not "Basic".</summary>
    [Theory]
    [InlineData(0, "yok")]
    [InlineData(1, "Kullanıcı")]
    [InlineData(2, "İş birimi")]
    [InlineData(4, "İş birimi ve altı")]
    [InlineData(8, "Kurum")]
    public void A_Depth_Mask_Is_Named_As_Crm_Names_It(int mask, string expected)
    {
        Assert.Equal(expected, RunAuthorityReport.Depth(mask));
    }

    /// <summary>
    /// No column names a person. The roles page answers "which group", and it does that with counts and team
    /// names; a delivered file listing every holder would carry the whole staff list out with it.
    /// </summary>
    [Fact]
    public void The_Roles_Page_Names_No_Person()
    {
        RunAuthority authority = new([new RunRole(Guid.NewGuid(), "Poliçe Operasyon", "Operasyon", 8, 8, 1200, ["Poliçe Ekibi"])], [], null);

        Sheet sheet = RunAuthorityReport.Roles(authority);

        Assert.DoesNotContain(sheet.Headers, header => header.Contains("kullanici_ad", StringComparison.Ordinal));
        Assert.Contains("kullanici_sayisi", sheet.Headers);
        Assert.Equal(1200, Rows(sheet)[0][sheet.Headers.ToList().IndexOf("kullanici_sayisi")]);
    }
}
