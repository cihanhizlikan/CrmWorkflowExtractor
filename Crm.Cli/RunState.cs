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

    public IReadOnlyList<WorkflowInventoryRecord> Records { get; set; } = [];

    /// <summary>Definitions routed to manual-review/ instead of the parser.</summary>
    public IReadOnlyList<Guid> ManualReview { get; set; } = [];

    /// <summary>IR documents produced by the offline stages.</summary>
    public IReadOnlyList<Crm.Ir.Model.WorkflowIr> Documents { get; set; } = [];

    /// <summary>Workflows whose XAML holds at least one sensitive literal (the values stay in the restricted report).</summary>
    public IReadOnlySet<Guid> SensitiveWorkflows { get; set; } = new HashSet<Guid>();

    /// <summary>How many steps of each workflow the parser did not understand.</summary>
    public IReadOnlyDictionary<Guid, int> UnmappedSteps { get; set; } = new Dictionary<Guid, int>();

    /// <summary>The combined BPMN file written for each family, by cluster id.</summary>
    public IReadOnlyDictionary<string, string> CombinedFiles { get; set; } = new Dictionary<string, string>();

    /// <summary>The BPMN file path (relative to the run folder, without extension) written for each workflow.</summary>
    public IReadOnlyDictionary<Guid, string> BpmnFiles { get; set; } = new Dictionary<Guid, string>();

    /// <summary>How many definitions fell into each usage verdict; the run report prints them.</summary>
    public IReadOnlyDictionary<string, int> UsageVerdicts { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>How far back the run evidence reaches, in words. Empty when no usage file was given.</summary>
    public string UsageHorizon { get; set; } = "";

    /// <summary>Sheets collected during the run; the offline stages gather them into the workbooks.</summary>
    public Dictionary<string, Crm.Cli.Reports.Sheet> Sheets { get; } = new(StringComparer.Ordinal);

    /// <summary>Named counts for every stage, ordinal-sorted so the manifest diffs cleanly. The §8 count chain reads from here.</summary>
    public SortedDictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

    public List<string> StagesRun { get; } = [];

    public List<string> Failures { get; } = [];

    public List<string> Warnings { get; } = [];

    /// <summary>Every final API response in order, written verbatim to raw/http/ at the end of the run.</summary>
    public List<CrmResponse> Responses { get; } = [];

    public Crm.Similarity.SimilarityResult? Similarity { get; set; }

    public IReadOnlyList<Crm.Cli.Reports.CountLink> CountChain { get; set; } = [];

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
