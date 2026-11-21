using System.Security.Cryptography;
using System.Text.Json;
using Crm.Cli;
using Crm.Cli.Configuration;
using Crm.Extract.Access;
using Crm.Extract.Runs;
using Crm.Tests.Fakes;
using Microsoft.Extensions.Options;
using Xunit;

namespace Crm.Tests.Cli;

public sealed class ExtractionRunTests
{
    /// <summary>A privilege the server does not name is a warning, written in Turkish like every other word of the run.</summary>
    [Fact]
    public void A_Privilege_The_Server_Does_Not_Name_Is_Warned_About_In_Turkish()
    {
        RunState state = new("20261121-000000", Path.GetTempPath(), "test");
        state.Privileges = [new PrivilegeFinding(new RequiredPrivilege("prvReadWorkflow", "workflow"), PrivilegeVerdict.Unverifiable, "")];

        ExtractionRun.ApplyPrivilegeVerdicts(state, requireOrganizationRead: true);

        Assert.Equal(["prvReadWorkflow (workflow): sunucuda bu adda bir yetki yok, bu yüzden derinliği denetlenemedi."], state.Warnings);
        Assert.Empty(state.Failures);
    }

    /// <summary>
    /// An output root that cannot be created — here, a path through an ordinary FILE — stops before any folder
    /// exists, says which path, and is a setting to fix rather than a failed run (sweep row T3).
    /// </summary>
    [Fact]
    public async Task An_Output_Root_That_Cannot_Be_Created_Exits_As_Configuration_Invalid_And_Names_It()
    {
        using TemporaryOutput output = new();
        Directory.CreateDirectory(output.Root);
        string blocker = Path.Combine(output.Root, "dosya");
        File.WriteAllText(blocker, "");
        string root = Path.Combine(blocker, "out");
        ExtractorSettings settings = new(
            Options.Create(new OutputOptions { Root = root }),
            Options.Create(new RunOptions { ImportFile = Path.Combine(output.Root, "yok.json") }));
        using StringWriter console = new();

        ExitCode code = await new ExtractionRun(settings, TimeProvider.System, console).RunAsync(CancellationToken.None);

        Assert.Equal(ExitCode.ConfigurationInvalid, code);
        Assert.StartsWith($"Çıktı klasörü oluşturulamadı: {root}.", console.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task A_Healthy_Organization_Produces_A_Sealed_Completed_Run()
    {
        FakeOrganization organization = new() { WorkflowCount = 45 };
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(organization, output);

        Assert.Equal(ExitCode.Success, code);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, "manifest.json")));
        JsonElement root = manifest.RootElement;
        Assert.Equal("completed", root.GetProperty("status").GetString());
        Assert.Equal("CORP\\svc-crm-read", root.GetProperty("authenticatedUser").GetProperty("domainName").GetString());
        Assert.Equal(45, root.GetProperty("counts").GetProperty("apiCount").GetInt32());
        Assert.Equal(45, root.GetProperty("counts").GetProperty("retrieved").GetInt32());
        Assert.Equal(45, File.ReadAllLines(Path.Combine(runRoot, "ham", "is-akislari.jsonl")).Length);
        Assert.Contains("$count 45 -> alınan 45", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_Artifact_Hash_In_The_Manifest_Matches_The_File_On_Disk()
    {
        FakeOrganization organization = new();
        using TemporaryOutput output = new();

        (_, string runRoot, _) = await RunHarness.RunAsync(organization, output);

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, "manifest.json")));
        List<JsonElement> artifacts = [.. manifest.RootElement.GetProperty("artifacts").EnumerateArray()];
        List<string> onDisk = [.. Directory.EnumerateFiles(runRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(runRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => path != "manifest.json")
            .Order(StringComparer.Ordinal)];
        Assert.Equal(onDisk, artifacts.Select(artifact => artifact.GetProperty("path").GetString()));
        Assert.Contains("gunlukler/calistirma.log", onDisk);
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
        FakeOrganization organization = new() { PrivilegeDepth = "Basic" };
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(organization, output);

        Assert.Equal(ExitCode.RunFailed, code);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, "manifest.json")));
        Assert.Equal("failed", manifest.RootElement.GetProperty("status").GetString());
        Assert.Contains("envanter", manifest.RootElement.GetProperty("stagesRun").EnumerateArray().Select(stage => stage.GetString()));
        Assert.Contains("prvReadWorkflow (Process) yetkisi 'Basic' derinliğinde", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Count_Mismatch_Fails_The_Run()
    {
        FakeOrganization organization = new() { WorkflowCount = 45, ReportedCount = 312 };
        using TemporaryOutput output = new();

        (ExitCode code, _, string console) = await RunHarness.RunAsync(organization, output);

        Assert.Equal(ExitCode.RunFailed, code);
        Assert.Contains("$count 312 iş akışı bildirdi, 45 kayıt alındı", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Single_Owner_Is_Printed_Prominently()
    {
        FakeOrganization organization = new() { DistinctOwners = 1 };
        using TemporaryOutput output = new();

        (_, _, string console) = await RunHarness.RunAsync(organization, output);

        Assert.Contains("YALNIZCA TEK BİR SAHİP", console, StringComparison.Ordinal);
        Assert.Contains("Farklı sahip:           1", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Column_Missing_On_The_Server_Is_Recorded_And_Warned()
    {
        FakeOrganization organization = new() { MissingAttributes = ["businessprocesstype"] };
        using TemporaryOutput output = new();

        (ExitCode code, string runRoot, string console) = await RunHarness.RunAsync(organization, output);

        Assert.Equal(ExitCode.Success, code);
        Assert.Contains("'businessprocesstype' sütunu", console, StringComparison.Ordinal);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, RunFolder.ManifestFileName)));
        Assert.Contains("businessprocesstype", manifest.RootElement.GetProperty("columnsMissingFromServer").EnumerateArray().Select(column => column.GetString()));
    }

    [Fact]
    public async Task A_Sealed_Run_Folder_Refuses_Further_Writes()
    {
        using TemporaryOutput output = new();
        RunFolder folder = RunFolder.Create(output.Root, DateTimeOffset.UtcNow);
        await folder.WriteTextAsync("reports/a.md", "a", CancellationToken.None);
        await folder.SealAsync(artifacts => new RunManifest(1, folder.RunId, RunStatus.Completed, "test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            null, null, "Default", null, "", [], [], [], [], null, new Dictionary<string, int>(), [], [], [], artifacts), CancellationToken.None);

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

    /// <summary>
    /// An exception nobody listed — here a usage file keyed by something that is not an id — used to leave Main as
    /// the runtime's crash: no manifest, nothing in the run log, an exit code that is not one of ours. The operator on
    /// the locked-down host sends back a folder, so the folder has to say what happened.
    /// </summary>
    [Fact]
    public async Task An_Unexpected_Exception_Fails_The_Run_And_Leaves_A_Sealed_Folder_That_Names_It()
    {
        FakeOrganization organization = new() { WorkflowCount = 3 };
        using TemporaryOutput output = new();
        Directory.CreateDirectory(output.Root);
        string usageFile = Path.Combine(output.Root, "usage.json");
        File.WriteAllText(usageFile, """{"format":"crm-usage-export/1","usage":{"not-an-id":{"jobs":[],"sessions":[]}}}""");

        (ExitCode code, string runRoot, _) = await RunHarness.RunAsync(organization, output, usageFile: usageFile);

        Assert.Equal(ExitCode.RunFailed, code);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, "manifest.json")));
        Assert.Equal("failed", manifest.RootElement.GetProperty("status").GetString());
        string log = File.ReadAllText(Path.Combine(runRoot, RunPaths.RunLog));
        Assert.Contains(nameof(FormatException), log, StringComparison.Ordinal);
        Assert.Contains(" at ", log, StringComparison.Ordinal);
    }
}
