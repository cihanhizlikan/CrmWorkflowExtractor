using Crm.Cli;
using Crm.Cli.Configuration;
using Crm.Extract.Http;
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

/// <summary>Runs <see cref="ExtractionRun"/> end to end against a fake server.</summary>
internal static class RunHarness
{
    public static async Task<(ExitCode Code, string RunRoot, string Console)> RunAsync(FakeCrmServer server, TemporaryOutput output, bool requirePrivileges = true, string reprocessRunId = "", string importFile = "", string usageFile = "")
    {
        CrmConnectionOptions crm = FakeOrganization.Options();
        ExtractorSettings settings = new(
            Options.Create(crm),
            Options.Create(new OutputOptions { Root = output.Root }),
            Options.Create(new RunOptions { RequireOrganizationReadPrivileges = requirePrivileges, ReprocessRunId = reprocessRunId, ImportFile = importFile, UsageFile = usageFile }));
        using StringWriter console = new();
        ExtractionRun run = new(settings, null, (options, _, logger) => FakeOrganization.ClientFor(server, options, logger), TimeProvider.System, console);

        ExitCode code = await run.RunAsync(CancellationToken.None);

        string runRoot = Directory.GetDirectories(Path.Combine(output.Root, "runs")).OrderByDescending(path => path, StringComparer.Ordinal).First();
        return (code, runRoot, console.ToString());
    }
}
