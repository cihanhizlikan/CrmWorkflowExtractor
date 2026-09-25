using Crm.Cli;
using Crm.Extract.Runs;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// The run-authority workbook, end to end, from the file the real browser script produced. What this proves that
/// the unit tests cannot: the export's security block survives the trip through <c>ham/yetkiler.json</c> and comes
/// out as a sheet, and the role that can only READ processes is still absent at the far end.
/// </summary>
public sealed class RunAuthorityWorkbookTests
{
    private static string Fixture()
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "BrowserExport", "mock-crm-export.json");
    }

    [Fact]
    public async Task The_Export_Security_Block_Arrives_As_A_Sheet()
    {
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(new FakeCrmServer(), output, importFile: Fixture());

        Assert.True(code == ExitCode.Success, console);
        Workbook workbook = Workbook.Open(Path.Combine(runRoot, RunPaths.RunAuthorityWorkbook));

        IReadOnlyList<IReadOnlyList<string>> roles = workbook.Rows("Çalıştırma yetkisi");
        Assert.Equal(3, roles.Count);
        Assert.Contains(roles, row => row.Contains("Poliçe Operasyon"));
        Assert.Contains(roles, row => row.Contains("Sistem Yöneticisi"));
        Assert.DoesNotContain(roles, row => row.Contains("Sadece Okuyan"));
        Assert.Contains(roles, row => row.Contains("İş birimi") && row.Contains("Poliçe Ekibi"));

        // Every workflow in the plan gets a row, and none of them is left blank about being startable by hand.
        IReadOnlyList<IReadOnlyList<string>> authority = workbook.Rows("Kim çalıştırabilir");
        Assert.True(authority.Count > 1, "the page has no workflow rows");
        Assert.All(authority.Skip(1), row => Assert.Contains(row[3], new[] { "evet", "hayır — yalnızca CRM tetikler" }));
    }
}
