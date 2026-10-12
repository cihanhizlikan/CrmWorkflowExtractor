using Crm.Ir.Model;

namespace Crm.Cli.Stages;

/// <summary>An active stage that fires a workflow, and the outcomes it fires it on.</summary>
public sealed record StageFiring(CaseStage Stage, string Outcomes)
{
    /// <summary>How the plan and the diagram name it: the request's subject, the stage, the outcomes.</summary>
    public string Text
    {
        get { return $"{(Stage.Process.Length > 0 ? Stage.Process : "?")} › {Stage.ShortName} ({Outcomes})"; }
    }
}
