using Crm.Cli;
using Crm.Cli.Configuration;
using Microsoft.Extensions.Options;

namespace Crm.Tests.Fakes;

/// <summary>A temporary output root, deleted afterwards.</summary>
internal sealed class TemporaryOutput : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "crm-extract-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

/// <summary>
/// Runs <see cref="ExtractionRun"/> end to end, the only way the tool now runs: from a file. An organization is
/// written as the export the browser script would have saved, and imported; a reprocess reads an earlier run.
/// </summary>
internal static class RunHarness
{
    /// <summary>The organization, exported and imported — or, with <paramref name="reprocessRunId"/>, an earlier run reprocessed.</summary>
    public static Task<(ExitCode Code, string RunRoot, string Console)> RunAsync(FakeOrganization organization, TemporaryOutput output,
        bool requirePrivileges = true, string reprocessRunId = "", string usageFile = "")
    {
        string importFile = reprocessRunId.Length > 0 ? "" : organization.WriteExport(Path.Combine(output.Root, "exports"));
        return RunAsync(output, importFile, requirePrivileges, reprocessRunId, usageFile);
    }

    /// <summary>A file the real browser script produced, or one a test built from it.</summary>
    public static Task<(ExitCode Code, string RunRoot, string Console)> ImportAsync(TemporaryOutput output, string importFile,
        bool requirePrivileges = true, string usageFile = "")
    {
        return RunAsync(output, importFile, requirePrivileges, "", usageFile);
    }

    private static async Task<(ExitCode Code, string RunRoot, string Console)> RunAsync(TemporaryOutput output, string importFile,
        bool requirePrivileges, string reprocessRunId, string usageFile)
    {
        ExtractorSettings settings = new(
            Options.Create(new OutputOptions { Root = output.Root }),
            Options.Create(new RunOptions { RequireOrganizationReadPrivileges = requirePrivileges, ReprocessRunId = reprocessRunId, ImportFile = importFile, UsageFile = usageFile }));
        using StringWriter console = new();
        ExtractionRun run = new(settings, TimeProvider.System, console);

        ExitCode code = await run.RunAsync(CancellationToken.None);

        string runRoot = Directory.GetDirectories(Path.Combine(output.Root, "runs")).OrderByDescending(path => path, StringComparer.Ordinal).First();
        return (code, runRoot, console.ToString());
    }
}
