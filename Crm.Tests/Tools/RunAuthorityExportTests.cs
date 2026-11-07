using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Tools;

/// <summary>
/// The browser export decides which security roles may start a process by hand, and nothing on the C# side can
/// check that: the reduction happens in the user's browser and only its result reaches the file. So the script
/// itself is run, UNCHANGED, under node (<c>export-harness.mjs</c>) against a fake CRM that answers GET only.
/// The requirement: starting a process needs <c>prvExecuteWorkflowJob</c>; a role that can only READ processes
/// (<c>prvReadWorkflow</c>) is never listed as able to RUN them, and a holder is counted, never named.
/// </summary>
public sealed class RunAuthorityExportTests
{
    private const string Run = "00000000-0000-0000-0000-0000000000a1";
    private const string Read = "00000000-0000-0000-0000-0000000000a2";
    private const string Runner = "00000000-0000-0000-0000-0000000000b1";
    private const string Reader = "00000000-0000-0000-0000-0000000000b2";
    private const string Starter = "00000000-0000-0000-0000-0000000000b3";
    private const string FirstUser = "00000000-0000-0000-0000-0000000000c1";
    private const string SecondUser = "00000000-0000-0000-0000-0000000000c2";
    private const string ReadingUser = "00000000-0000-0000-0000-0000000000c3";

    private const string Grants = $$"""
        [{ "roleid": "{{Runner}}", "privilegeid": "{{Run}}", "privilegedepthmask": 8 },
         { "roleid": "{{Runner}}", "privilegeid": "{{Read}}", "privilegedepthmask": 4 },
         { "roleid": "{{Reader}}", "privilegeid": "{{Read}}", "privilegedepthmask": 8 },
         { "roleid": "{{Starter}}", "privilegeid": "{{Run}}", "privilegedepthmask": 1 }]
        """;

    private const string ReadOnlyGrants = $$"""
        [{ "roleid": "{{Reader}}", "privilegeid": "{{Read}}", "privilegedepthmask": 8 }]
        """;

    [Fact]
    public async Task A_Role_That_Can_Only_Read_Processes_Is_Never_Listed_As_Able_To_Run_Them()
    {
        JsonElement authority = await RunAuthorityAsync(Grants);

        JsonElement[] roles = [.. authority.GetProperty("roles").EnumerateArray()];
        Assert.Equal(["İş Akışı Çalıştıran", "Süreç Başlatan"], roles.Select(role => role.GetProperty("name").GetString()));
        Assert.DoesNotContain(roles, role => role.GetProperty("roleId").GetString() == Reader);

        JsonElement runner = roles[0];
        Assert.Equal(2, runner.GetProperty("users").GetInt32());
        Assert.Equal(8, runner.GetProperty("runDepthMask").GetInt32());
        Assert.Equal(4, runner.GetProperty("processDepthMask").GetInt32());
        Assert.Equal(["Çağrı Merkezi"], runner.GetProperty("teams").EnumerateArray().Select(team => team.GetString()));

        JsonElement starter = roles[1];
        Assert.Equal(0, starter.GetProperty("users").GetInt32());
        Assert.Equal(0, starter.GetProperty("processDepthMask").GetInt32());
    }

    [Fact]
    public async Task The_Holders_Of_A_Role_Are_Counted_And_Never_Named()
    {
        JsonElement authority = await RunAuthorityAsync(Grants);

        string written = authority.GetRawText();
        Assert.DoesNotContain(FirstUser, written, StringComparison.Ordinal);
        Assert.DoesNotContain(SecondUser, written, StringComparison.Ordinal);
        Assert.DoesNotContain(ReadingUser, written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_Estate_Where_Roles_Only_Read_Processes_Lists_No_Role_And_Says_Why()
    {
        JsonElement authority = await RunAuthorityAsync(ReadOnlyGrants);

        Assert.Empty(authority.GetProperty("roles").EnumerateArray());
        Assert.Contains("prvExecuteWorkflowJob", authority.GetProperty("note").GetString(), StringComparison.Ordinal);
    }

    private static string Scenario(string grants)
    {
        return $$"""
            { "routes": {
              "EntityDefinitions?$select=LogicalName,EntitySetName": { "value": [
                { "LogicalName": "roleprivileges", "EntitySetName": "roleprivilegescollection" },
                { "LogicalName": "systemuserroles", "EntitySetName": "systemuserrolescollection" },
                { "LogicalName": "teamroles", "EntitySetName": "teamrolescollection" }] },
              "privileges?$select=privilegeid,name&$filter=name%20eq%20'prvExecuteWorkflowJob'": { "value": [
                { "privilegeid": "{{Run}}", "name": "prvExecuteWorkflowJob" },
                { "privilegeid": "{{Read}}", "name": "prvReadWorkflow" }] },
              "roleprivilegescollection?": { "value": {{grants}} },
              "roles?": { "value": [
                { "roleid": "{{Runner}}", "name": "İş Akışı Çalıştıran" },
                { "roleid": "{{Reader}}", "name": "Süreç Okuyucu" },
                { "roleid": "{{Starter}}", "name": "Süreç Başlatan" }] },
              "systemuserrolescollection?": { "value": [
                { "roleid": "{{Runner}}", "systemuserid": "{{FirstUser}}" },
                { "roleid": "{{Runner}}", "systemuserid": "{{SecondUser}}" },
                { "roleid": "{{Reader}}", "systemuserid": "{{ReadingUser}}" }] },
              "teamrolescollection?": { "value": [{ "roleid": "{{Runner}}", "teamid": "t1" }] },
              "teams?": { "value": [{ "teamid": "t1", "name": "Çağrı Merkezi", "teamtype": 0 }] }
            } }
            """;
    }

    private static async Task<JsonElement> RunAuthorityAsync(string grants)
    {
        using TemporaryOutput output = new();
        Directory.CreateDirectory(output.Root);
        string scenario = Path.Combine(output.Root, "scenario.json");
        await File.WriteAllTextAsync(scenario, Scenario(grants), TestContext.Current.CancellationToken);

        string root = RepositoryTree.Root().FullName;
        ProcessStartInfo start = new("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(root, "Crm.Tests", "Tools", "export-harness.mjs"));
        start.ArgumentList.Add(Path.Combine(root, "tools", "crm-browser-export.js"));
        start.ArgumentList.Add(scenario);

        Process? started;
        try
        {
            started = Process.Start(start);
        }
        catch (Win32Exception)
        {
            Assert.Skip("node is not on PATH; the browser scripts cannot be run on this machine.");
            throw;
        }
        using Process process = started ?? throw new InvalidOperationException("node did not start.");

        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(TimeSpan.FromMinutes(1));
        Task<string> document = process.StandardOutput.ReadToEndAsync(limit.Token);
        Task<string> console = process.StandardError.ReadToEndAsync(limit.Token);
        await process.WaitForExitAsync(limit.Token);

        Assert.True(process.ExitCode == 0, await console);
        using JsonDocument exported = JsonDocument.Parse(await document);
        return exported.RootElement.GetProperty("runAuthority").Clone();
    }
}
