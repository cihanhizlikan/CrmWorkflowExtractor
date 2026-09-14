using System.Text.Json;

namespace Crm.Extract.Inventory;

/// <summary>A server label observed for one option-set value, kept to compare the handout's tables with the live system.</summary>
public sealed record ObservedOption(string Column, int Raw, string ToolLabel, string? ServerLabel);

/// <summary>The fields of one <c>workflows</c> record that classification and reconciliation need. The full record stays verbatim in raw/.</summary>
public sealed record WorkflowInventoryRecord(
    Guid WorkflowId,
    string Name,
    string? PrimaryEntity,
    OptionValue Category,
    OptionValue Type,
    OptionValue Mode,
    OptionValue Scope,
    OptionValue State,
    Guid? OwnerId,
    Guid? ParentWorkflowId,
    Guid? ActiveWorkflowId,
    bool? IsCrmUiWorkflow,
    long? VersionNumber,
    IReadOnlyList<ObservedOption> ObservedOptions)
{
    public static WorkflowInventoryRecord Parse(JsonElement record)
    {
        Guid workflowId = Json.OptionalGuid(record, "workflowid")
            ?? throw new InvalidDataException("A workflows record has no workflowid: " + record.GetRawText());

        List<ObservedOption> observed = [];
        OptionValue Option(string column)
        {
            OptionValue value = WorkflowOptionSets.Describe(column, Json.OptionalInt(record, column));
            if (value.Raw is int raw)
            {
                observed.Add(new ObservedOption(column, raw, value.Label, Json.FormattedValue(record, column)));
            }
            return value;
        }

        OptionValue category = Option("category");
        OptionValue type = Option("type");
        OptionValue mode = Option("mode");
        OptionValue scope = Option("scope");
        OptionValue state = Option("statecode");
        Option("runas");
        Option("createstage");
        Option("updatestage");
        Option("deletestage");

        return new WorkflowInventoryRecord(
            workflowId,
            Json.OptionalString(record, "name") ?? "",
            Json.OptionalString(record, "primaryentity"),
            category,
            type,
            mode,
            scope,
            state,
            Json.OptionalGuid(record, "_ownerid_value"),
            Json.OptionalGuid(record, "_parentworkflowid_value"),
            Json.OptionalGuid(record, "_activeworkflowid_value"),
            Json.OptionalBool(record, "iscrmuiworkflow"),
            Json.OptionalLong(record, "versionnumber"),
            observed);
    }
}
