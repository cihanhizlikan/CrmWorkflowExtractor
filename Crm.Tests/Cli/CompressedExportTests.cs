using System.IO.Compression;
using System.Text.Json;
using Crm.Cli;
using Crm.Extract.Runs;
using Crm.Tests.Fakes;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// The browser compresses the export when it can: six thousand XAML documents are some six hundred megabytes of
/// text and about forty compressed, and the uncompressed file was large enough that writing it killed the tab.
/// A compressed export must import to exactly the same run as the plain one, and must be recognised by what it
/// IS rather than what it is called — an analyst who renames the file should not silently get a broken run.
/// </summary>
public sealed class CompressedExportTests
{
    private static string Fixture()
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "BrowserExport", "mock-crm-export.json");
    }

    private static string Gzip(string name)
    {
        string path = Path.Combine(Path.GetTempPath(), name);
        using (FileStream source = File.OpenRead(Fixture()))
        using (FileStream target = File.Create(path))
        using (GZipStream zip = new(target, CompressionLevel.Optimal))
        {
            source.CopyTo(zip);
        }
        return path;
    }

    [Fact]
    public async Task A_Compressed_Export_Imports_To_The_Same_Run_As_A_Plain_One()
    {
        using TemporaryOutput plainOutput = new();
        using TemporaryOutput zippedOutput = new();

        (ExitCode plainCode, string plainRoot, string plainConsole) = await RunHarness.ImportAsync(plainOutput, Fixture());
        (ExitCode zippedCode, string zippedRoot, string zippedConsole) = await RunHarness.ImportAsync(zippedOutput, Gzip("mock-crm-export.json.gz"));

        Assert.True(plainCode == ExitCode.Success, plainConsole);
        Assert.True(zippedCode == ExitCode.Success, zippedConsole);
        Assert.Equal(Counts(plainRoot), Counts(zippedRoot));
        Assert.Equal(
            Directory.GetFiles(Path.Combine(plainRoot, "bpmn"), "*.bpmn", SearchOption.AllDirectories).Length,
            Directory.GetFiles(Path.Combine(zippedRoot, "bpmn"), "*.bpmn", SearchOption.AllDirectories).Length);
    }

    /// <summary>The evidence stays verbatim — the bytes the user's browser wrote — under a name that says so.</summary>
    [Fact]
    public async Task The_Compressed_Export_Is_Kept_Verbatim_Under_Its_Own_Name()
    {
        using TemporaryOutput output = new();
        string zipped = Gzip("mock-crm-export.json.gz");

        (ExitCode code, string runRoot, string console) = await RunHarness.ImportAsync(output, zipped);

        Assert.True(code == ExitCode.Success, console);
        string evidence = Path.Combine(runRoot, RunPaths.RawBrowserExport + ".gz");
        Assert.True(File.Exists(evidence), $"{evidence} yazılmadı");
        Assert.Equal(File.ReadAllBytes(zipped), File.ReadAllBytes(evidence));
    }

    /// <summary>
    /// Recognised by its first two bytes, not its name. A compressed export saved as plain ".json" still imports.
    /// </summary>
    [Fact]
    public async Task A_Compressed_Export_Named_Json_Is_Still_Read()
    {
        using TemporaryOutput output = new();

        (ExitCode code, _, string console) = await RunHarness.ImportAsync(output, Gzip("misnamed-export.json"));

        Assert.True(code == ExitCode.Success, console);
    }

    private static string Counts(string runRoot)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(runRoot, RunFolder.ManifestFileName)));
        return manifest.RootElement.GetProperty("stageCounts").GetRawText();
    }
}
