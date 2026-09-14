using Crm.Extract.Http;
using Crm.Extract.Inventory;
using Crm.Extract.Preflight;
using Crm.Extract.Runs;

namespace Crm.Cli;

/// <summary>Everything one run learns, accumulated stage by stage and turned into the summary and the manifest at the end.</summary>
public sealed class RunState(string runId, string runRoot, string toolVersion)
{
    public string RunId { get; } = runId;

    public string RunRoot { get; } = runRoot;

    public string ToolVersion { get; } = toolVersion;

    public string? OrganizationUrl { get; set; }

    public CrmIdentity? Identity { get; set; }

    public IReadOnlyList<PrivilegeFinding> Privileges { get; set; } = [];

    public IReadOnlyList<string> ColumnsSelected { get; set; } = [];

    public IReadOnlyList<string> ColumnsMissing { get; set; } = [];

    public ReconciliationResult? Reconciliation { get; set; }

    public List<string> StagesRun { get; } = [];

    public List<string> Failures { get; } = [];

    public List<string> Warnings { get; } = [];

    /// <summary>Every final API response in order, written verbatim to raw/http/ at the end of the run.</summary>
    public List<CrmResponse> Responses { get; } = [];

    public ExitCode ExitCode { get; set; } = ExitCode.Success;

    public string Status
    {
        get { return Failures.Count == 0 && ExitCode == ExitCode.Success ? RunStatus.Completed : RunStatus.Failed; }
    }

    public void Fail(ExitCode code, string message)
    {
        Failures.Add(message);
        if (ExitCode == ExitCode.Success)
        {
            ExitCode = code;
        }
    }
}
