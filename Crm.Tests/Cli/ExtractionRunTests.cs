using System.Security.Cryptography;
using System.Text.Json;
using Crm.Cli;
using Crm.Extract.Runs;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

public sealed class ExtractionRunTests
{
    [Fact]
    public async Task A_Healthy_Organization_Produces_A_Sealed_Completed_Run()
    {
        FakeCrmServer server = new FakeOrganization { WorkflowCount = 45 }.Build();
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(server, output);

        Assert.Equal(ExitCode.Success, code);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, "manifest.json")));
        JsonElement root = manifest.RootElement;
        Assert.Equal("completed", root.GetProperty("status").GetString());
        Assert.Equal("CORP\\svc-crm-read", root.GetProperty("authenticatedUser").GetProperty("domainName").GetString());
        Assert.Equal(45, root.GetProperty("counts").GetProperty("apiCount").GetInt32());
        Assert.Equal(45, root.GetProperty("counts").GetProperty("retrieved").GetInt32());
        Assert.Equal(45, File.ReadAllLines(Path.Combine(runRoot, "raw", "workflows.jsonl")).Length);
        Assert.Contains("$count 45 -> retrieved 45", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_Artifact_Hash_In_The_Manifest_Matches_The_File_On_Disk()
    {
        FakeCrmServer server = new FakeOrganization().Build();
        using TemporaryOutput output = new();

        (_, string runRoot, _) = await RunHarness.RunAsync(server, output);

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, "manifest.json")));
        List<JsonElement> artifacts = [.. manifest.RootElement.GetProperty("artifacts").EnumerateArray()];
        List<string> onDisk = [.. Directory.EnumerateFiles(runRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(runRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => path != "manifest.json")
            .Order(StringComparer.Ordinal)];
        Assert.Equal(onDisk, artifacts.Select(artifact => artifact.GetProperty("path").GetString()));
        Assert.Contains("logs/run.log", onDisk);
        foreach (JsonElement artifact in artifacts)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(runRoot, artifact.GetProperty("path").GetString()!));
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), artifact.GetProperty("sha256").GetString());
        }
    }

    /// <summary>§2.4 end to end: user-level read makes the run fail, but the inventory still runs so its count can be compared.</summary>
    [Fact]
    public async Task User_Level_Read_Fails_The_Run_But_Keeps_The_Inventory_As_Evidence()
    {
        FakeCrmServer server = new FakeOrganization { PrivilegeDepth = "Basic" }.Build();
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(server, output);

        Assert.Equal(ExitCode.RunFailed, code);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, "manifest.json")));
        Assert.Equal("failed", manifest.RootElement.GetProperty("status").GetString());
        Assert.Contains("inventory", manifest.RootElement.GetProperty("stagesRun").EnumerateArray().Select(stage => stage.GetString()));
        Assert.Contains("prvReadWorkflow (Process) is held at depth 'Basic'", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Count_Mismatch_Fails_The_Run()
    {
        FakeCrmServer server = new FakeOrganization { WorkflowCount = 45, ReportedCount = 312 }.Build();
        using TemporaryOutput output = new();

        (ExitCode code, _, string console) = await RunHarness.RunAsync(server, output);

        Assert.Equal(ExitCode.RunFailed, code);
        Assert.Contains("$count reported 312 workflows but 45 were retrieved", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Single_Owner_Is_Printed_Prominently()
    {
        FakeCrmServer server = new FakeOrganization { DistinctOwners = 1 }.Build();
        using TemporaryOutput output = new();

        (_, _, string console) = await RunHarness.RunAsync(server, output);

        Assert.Contains("ONLY ONE DISTINCT OWNER", console, StringComparison.Ordinal);
        Assert.Contains("Distinct owners:        1", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Column_Missing_On_The_Server_Is_Left_Out_Of_The_Query_And_Warned()
    {
        FakeCrmServer server = new FakeOrganization { MissingAttributes = ["businessprocesstype"] }.Build();
        using TemporaryOutput output = new();

        (ExitCode code, _, string console) = await RunHarness.RunAsync(server, output);

        Assert.Equal(ExitCode.Success, code);
        Assert.Contains("Column 'businessprocesstype'", console, StringComparison.Ordinal);
        Assert.DoesNotContain(server.Requests, request => Uri.UnescapeDataString(request.Uri.Query).Contains("businessprocesstype", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_Sealed_Run_Folder_Refuses_Further_Writes()
    {
        using TemporaryOutput output = new();
        RunFolder folder = RunFolder.Create(output.Root, DateTimeOffset.UtcNow);
        await folder.WriteTextAsync("reports/a.md", "a", CancellationToken.None);
        await folder.SealAsync(artifacts => new RunManifest(1, folder.RunId, RunStatus.Completed, "test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            null, null, "Default", null, "", [], [], [], [], null, [], [], artifacts), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => folder.WriteTextAsync("reports/b.md", "b", CancellationToken.None));
    }

    [Fact]
    public void Two_Runs_In_The_Same_Second_Never_Share_A_Folder()
    {
        using TemporaryOutput output = new();
        DateTimeOffset now = new(2026, 9, 14, 10, 15, 0, TimeSpan.Zero);

        RunFolder first = RunFolder.Create(output.Root, now);
        RunFolder second = RunFolder.Create(output.Root, now);

        Assert.Equal("20260914-101500", first.RunId);
        Assert.Equal("20260914-101500-2", second.RunId);
    }
}
