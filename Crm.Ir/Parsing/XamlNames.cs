using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Crm.Ir.Parsing;

/// <summary>Name-level facts about designer XAML: display names, keys, activity types, and which assemblies are Microsoft's.</summary>
internal static partial class XamlNames
{
    public static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Elements that carry a step's machinery rather than being a step. Consumed by the step that holds them.</summary>
    public static readonly HashSet<string> Support = new(StringComparer.Ordinal)
    {
        "GetEntityProperty", "SetEntityProperty", "EvaluateCondition", "EvaluateLogicalCondition", "EvaluateExpression",
        "Assign", "Persist", "Variable", "Collection", "InArgument", "OutArgument", "InOutArgument", "ReferenceLiteral",
        "Literal", "VisualBasicValue", "VisualBasicReference", "Null", "String", "Boolean", "Int32", "Members", "Property",
        "Composite", "ConditionBranch", "Workflow", "Activity", "Type", "Object",
        // Seen on the production 8.2 server (2026-09-22): conversion between Xrm and CRM types, and typed literals.
        "ConvertCrmXrmTypes", "OptionSetValue", "XrmTimeSpan",
        // Loads a related record so later steps can read its fields; recorded as a read in DataTouched.
        "RetrieveEntity"
    };

    /// <summary>Elements that are themselves the evidence of an out-of-the-box step.</summary>
    public static readonly HashSet<string> StepEvidence = new(StringComparer.Ordinal)
    {
        "UpdateEntity", "CreateEntity", "AssignEntity", "SetState", "SendEmail", "StartChildWorkflow", "TerminateWorkflow", "SendEmailFromTemplate"
    };

    public static string DisplayName(XElement element)
    {
        return element.Attribute("DisplayName")?.Value ?? "";
    }

    public static string? Key(XElement element)
    {
        return element.Attribute(Xaml + "Key")?.Value;
    }

    public static string? AssemblyQualifiedName(XElement element)
    {
        return element.Name.LocalName == "ActivityReference" ? element.Attribute("AssemblyQualifiedName")?.Value : null;
    }

    /// <summary><c>Microsoft.Crm.Workflow.Activities.ConditionSequence, Microsoft.Crm.Workflow, …</c> → <c>ConditionSequence</c>.</summary>
    public static string ShortTypeName(string assemblyQualifiedName)
    {
        string typeName = FullTypeName(assemblyQualifiedName);
        int dot = typeName.LastIndexOf('.');
        return dot < 0 ? typeName : typeName[(dot + 1)..];
    }

    public static string FullTypeName(string assemblyQualifiedName)
    {
        int comma = assemblyQualifiedName.IndexOf(',', StringComparison.Ordinal);
        return (comma < 0 ? assemblyQualifiedName : assemblyQualifiedName[..comma]).Trim();
    }

    public static string? AssemblyOf(string assemblyQualifiedName)
    {
        string[] parts = assemblyQualifiedName.Split(',');
        return parts.Length > 1 ? parts[1].Trim() : null;
    }

    public static bool IsMicrosoftAssembly(string? assembly)
    {
        return assembly is null
            || assembly.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
            || assembly.StartsWith("System", StringComparison.OrdinalIgnoreCase)
            || assembly.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The assembly of a <c>clr-namespace:X;assembly=Y, …</c> XML namespace, or null for any other namespace.</summary>
    public static (string ClrNamespace, string Assembly)? ClrNamespaceOf(XNamespace xmlNamespace)
    {
        Match match = ClrNamespacePattern().Match(xmlNamespace.NamespaceName);
        return match.Success ? (match.Groups["ns"].Value, match.Groups["asm"].Value.Trim()) : null;
    }

    /// <summary>A partner custom workflow activity written as its own element (not a property element like <c>Foo.Bar</c>).</summary>
    public static bool IsCustomActivityElement(XElement element)
    {
        return !element.Name.LocalName.Contains('.', StringComparison.Ordinal)
            && ClrNamespaceOf(element.Name.Namespace) is (_, string assembly)
            && !IsMicrosoftAssembly(assembly);
    }

    /// <summary>The construct name used in coverage: an ActivityReference's type, otherwise the element's local name.</summary>
    public static string ConstructName(XElement element)
    {
        string? aqn = AssemblyQualifiedName(element);
        if (aqn is not null)
        {
            return ShortTypeName(aqn);
        }
        return element.Name.LocalName;
    }

    public static bool IsPropertyElement(XElement element)
    {
        return element.Name.LocalName.Contains('.', StringComparison.Ordinal);
    }

    /// <summary>Designer step display names: <c>UpdateStep3: Poliçeyi askıya al</c> → kind <c>Update</c>, description <c>Poliçeyi askıya al</c>.</summary>
    public static (string Kind, string? Description)? StepName(string displayName)
    {
        Match match = StepPattern().Match(displayName);
        if (!match.Success)
        {
            return null;
        }
        string description = match.Groups["desc"].Value.Trim();
        return (match.Groups["kind"].Value, description.Length == 0 ? null : description);
    }

    /// <summary>The argument or property element of an ActivityReference with the given <c>x:Key</c>.</summary>
    public static XElement? Keyed(XElement activityReference, string container, string key)
    {
        return activityReference.Elements()
            .Where(child => child.Name.LocalName == "ActivityReference." + container)
            .SelectMany(child => child.Elements())
            .FirstOrDefault(child => string.Equals(Key(child), key, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"^clr-namespace:(?<ns>[^;]*);assembly=(?<asm>[^,]+)")]
    private static partial Regex ClrNamespacePattern();

    [GeneratedRegex(@"^(?<kind>[A-Za-z]+?)Step(?<n>\d+)(?::\s*(?<desc>.*))?$", RegexOptions.Singleline)]
    private static partial Regex StepPattern();
}
