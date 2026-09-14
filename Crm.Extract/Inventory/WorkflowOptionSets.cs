using System.Globalization;

namespace Crm.Extract.Inventory;

/// <summary>An option-set value: the raw integer always, a label always, and whether the label came from the known table.</summary>
public sealed record OptionValue(int? Raw, string Label, bool IsKnown)
{
    public override string ToString()
    {
        return Label;
    }
}

/// <summary>
/// The §3.1 option-set tables for CRM 8.2. A value outside them becomes <c>Unknown(n)</c> and a warning — never a
/// crash, never a silent coercion. The server's own labels are captured beside these (FormattedValue annotations),
/// so a disagreement between the handout and the live system is visible in the run output.
/// </summary>
public static class WorkflowOptionSets
{
    private static readonly IReadOnlyDictionary<int, string> Stage = new Dictionary<int, string>
    {
        [20] = "Pre-operation",
        [40] = "Post-operation"
    };

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> ByColumn =
        new Dictionary<string, IReadOnlyDictionary<int, string>>(StringComparer.Ordinal)
        {
            ["category"] = new Dictionary<int, string>
            {
                [0] = "Workflow",
                [1] = "Dialog",
                [2] = "Business Rule",
                [3] = "Action",
                [4] = "Business Process Flow"
            },
            ["type"] = new Dictionary<int, string>
            {
                [1] = "Definition",
                [2] = "Activation",
                [3] = "Template"
            },
            ["mode"] = new Dictionary<int, string>
            {
                [0] = "Background",
                [1] = "Real-time"
            },
            ["scope"] = new Dictionary<int, string>
            {
                [1] = "User",
                [2] = "Business Unit",
                [3] = "Parent: Child Business Units",
                [4] = "Organization"
            },
            ["createstage"] = Stage,
            ["updatestage"] = Stage,
            ["deletestage"] = Stage,
            ["runas"] = new Dictionary<int, string>
            {
                [0] = "Owner",
                [1] = "Calling User"
            },
            ["statecode"] = new Dictionary<int, string>
            {
                [0] = "Draft",
                [1] = "Activated"
            }
        };

    public const int CategoryWorkflow = 0;
    public const int TypeDefinition = 1;
    public const int TypeActivation = 2;
    public const int TypeTemplate = 3;

    public static OptionValue Describe(string column, int? raw)
    {
        if (raw is not int value)
        {
            return new OptionValue(null, "(null)", true);
        }
        if (ByColumn.TryGetValue(column, out IReadOnlyDictionary<int, string>? table) && table.TryGetValue(value, out string? label))
        {
            return new OptionValue(value, label, true);
        }
        return new OptionValue(value, string.Create(CultureInfo.InvariantCulture, $"Unknown({value})"), false);
    }
}
