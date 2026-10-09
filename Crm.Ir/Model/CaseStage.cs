namespace Crm.Ir.Model;

/// <summary>
/// One stage of the machine a case moves through: a record of Pensionsoft's <c>ps_step</c>, not anything in a
/// workflow. <see cref="Process"/> is the case subcategory the stage belongs to (its <c>Konu</c>); <see cref="Primary"/>
/// marks the stage a case of that subcategory starts in. <see cref="Fields"/> holds every field of the record as a
/// reader would see it — the option's label, the lookup's name — keyed by logical name; the stage's SLA, document,
/// SMS and assignment rules are read from there.
/// </summary>
public sealed record CaseStage(
    Guid Id,
    string Name,
    string ShortName,
    Guid? ProcessId,
    string Process,
    bool Primary,
    bool Automatic,
    bool Active,
    IReadOnlyList<StageExit> Exits,
    IReadOnlyDictionary<string, string> Fields);

/// <summary>
/// Where a stage goes on one outcome, and what it fires on the way. Either half may be absent: an outcome can end
/// the case without moving it on, and a stage can move on without running anything.
/// </summary>
public sealed record StageExit(string Outcome, Guid? NextStage, string? NextStageName, Guid? Workflow, string? WorkflowName, string? Sms);

/// <summary>The three outcomes a stage is closed with, in the words this tool writes them in.</summary>
public static class StageOutcome
{
    public const string Success = "olumlu";

    public const string Failure = "olumsuz";

    public const string Cancel = "iptal";
}
