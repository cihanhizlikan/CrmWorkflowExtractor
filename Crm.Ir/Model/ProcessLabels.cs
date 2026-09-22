namespace Crm.Ir.Model;

/// <summary>
/// The option labels this tool writes for a process's category, mode and state. They are the same strings
/// <c>Crm.Extract</c>'s §3.1 tables produce — that project cannot be referenced from here (see structure.md), so
/// <c>OptionLabelTests</c> pins the two together. Anything that branches on a category or a state uses these, so a
/// wording change can never quietly change behaviour.
/// </summary>
public static class ProcessLabels
{
    public const string CategoryWorkflow = "İş Akışı";
    public const string CategoryDialog = "Diyalog";
    public const string CategoryBusinessRule = "İş Kuralı";
    public const string CategoryAction = "Eylem";
    public const string CategoryBusinessProcessFlow = "İş Süreci Akışı";

    public const string ModeBackground = "Arka plan";
    public const string ModeRealTime = "Gerçek zamanlı";

    public const string StateDraft = "Taslak";
    public const string StateActivated = "Etkin";
}
