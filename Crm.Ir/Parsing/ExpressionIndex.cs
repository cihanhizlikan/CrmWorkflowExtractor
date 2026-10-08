using System.Text.RegularExpressions;
using System.Xml.Linq;
using Crm.Ir.Text;

namespace Crm.Ir.Parsing;

/// <summary>An attribute read into a variable by <c>GetEntityProperty</c>.</summary>
internal sealed record AttributeRead(string Entity, string Attribute);

/// <summary>
/// The activity that wrote a variable and which of its output arguments did — <c>NotifyPolicyService.Sonuc</c>.
/// A condition whose operand came from an activity rather than from a field has no attribute name to show, and
/// this is the next best one: without it the diagram showed a bare question mark, which names nothing.
/// </summary>
internal sealed record VariableSource(string Activity, string Argument)
{
    public string Text
    {
        get { return Argument.Length == 0 ? Activity : $"{Activity}.{Argument}"; }
    }
}

/// <summary>A comparison written by <c>EvaluateCondition</c>: operand variable, operator, parameter variables.</summary>
internal sealed record Comparison(string OperandVariable, string Operator, IReadOnlyList<string> ParameterVariables);

/// <summary>A boolean combination written by <c>EvaluateLogicalCondition</c>.</summary>
internal sealed record LogicalCombination(string Operator, string Left, string Right);

/// <summary>
/// The designer does not write values inline: it assigns them to generated variables (<c>ConditionBranchStep2_1</c>)
/// through helper activities and refers to the variables. This index follows those assignments across the whole
/// document once, so a step can ask "which attribute is in this variable" or "which literal".
/// </summary>
internal sealed partial class ExpressionIndex
{
    public const string Dynamic = "<dynamic>";

    public Dictionary<string, AttributeRead> Reads { get; } = new(StringComparer.Ordinal);

    /// <summary>Variable → the activity and output argument that wrote it, for variables no field read fills.</summary>
    public Dictionary<string, VariableSource> Writers { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, IReadOnlyList<string>> Literals { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Comparison> Comparisons { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, LogicalCombination> Logicals { get; } = new(StringComparer.Ordinal);

    /// <summary><c>ConvertCrmXrmTypes</c> result variable → the variable it converts: the same value in another type.</summary>
    private readonly Dictionary<string, string> _conversions = new(StringComparer.Ordinal);

    public static ExpressionIndex Build(XElement root)
    {
        ExpressionIndex index = new();
        foreach (XElement element in root.Descendants())
        {
            index.AddWriters(element);
            if (element.Name.LocalName == "GetEntityProperty")
            {
                index.AddRead(element);
                continue;
            }
            string? aqn = XamlNames.AssemblyQualifiedName(element);
            if (aqn is null)
            {
                continue;
            }
            string type = XamlNames.ShortTypeName(aqn);
            if (type == "EvaluateExpression")
            {
                index.AddExpression(element);
            }
            else if (type == "EvaluateCondition")
            {
                index.AddComparison(element);
            }
            else if (type == "EvaluateLogicalCondition")
            {
                index.AddLogical(element);
            }
            else if (type == "ConvertCrmXrmTypes")
            {
                index.AddConversion(element);
            }
        }
        index.ResolveConversions();
        return index;
    }

    /// <summary><c>[ConditionBranchStep2_1]</c> or <c>ConditionBranchStep2_1</c> → the variable name; anything else → null.</summary>
    public static string? VariableOf(string? expression)
    {
        if (expression is null)
        {
            return null;
        }
        string trimmed = expression.Trim().TrimStart('[').TrimEnd(']').Trim();
        return Identifier().IsMatch(trimmed) ? trimmed : null;
    }

    /// <summary>
    /// A variable REFERENCE and nothing else: <c>[ConditionBranchStep12_1]</c>. The brackets are what make it one.
    /// Without them <c>Attribute="ps_activitytypeid"</c> is a plain identifier too, and reading it as a variable
    /// indexed a field name as though it held the field's value — wrong, and quietly so.
    /// </summary>
    public static string? Bracketed(string raw)
    {
        string trimmed = raw.Trim();
        return trimmed.StartsWith('[') && trimmed.EndsWith(']') ? VariableOf(trimmed) : null;
    }

    /// <summary>Every variable an expression mentions, in order: <c>[New Object() { A_1, A_2 }]</c> → A_1, A_2.</summary>
    public static IReadOnlyList<string> VariablesIn(string? expression)
    {
        if (expression is null)
        {
            return [];
        }
        return [.. StepVariable().Matches(expression).Select(match => match.Value)];
    }

    public static IReadOnlyList<string> QuotedStrings(string expression)
    {
        return [.. Quoted().Matches(expression)
            .Select(match => HtmlEntities.Decode(match.Groups[1].Value.Replace("\"\"", "\"", StringComparison.Ordinal)))];
    }

    /// <summary>The value(s) held by a variable: literals when the designer created them, <see cref="Dynamic"/> otherwise.</summary>
    public IReadOnlyList<string> ValuesOf(string? variable)
    {
        if (variable is not null && Literals.TryGetValue(variable, out IReadOnlyList<string>? values))
        {
            return values;
        }
        return [Dynamic];
    }

    private static string? ArgumentText(XElement activityReference, string key)
    {
        XElement? argument = XamlNames.Keyed(activityReference, "Arguments", key);
        return argument is null ? null : string.Concat(argument.DescendantNodesAndSelf().OfType<XText>().Select(text => text.Value));
    }

    /// <summary>
    /// The field a condition reads, whichever way the designer wrote it down. The variable it reads into is
    /// normally a property ELEMENT — <c>&lt;GetEntityProperty.Value&gt;&lt;OutArgument&gt;…</c> — but this
    /// organisation's definitions write it as an ATTRIBUTE of the same element instead, and reading only the
    /// element form left every one of those conditions with no field to name. The diagram then asked about
    /// <c>ConditionBranchStep15_1</c>, the designer's own generated slot, which names nothing.
    ///
    /// <para>
    /// The attribute is found by SHAPE rather than by name, but the shape has to be <see cref="Bracketed"/>:
    /// <c>Attribute="ps_activitytypeid"</c> is a perfectly good identifier too, and matching on that indexed a
    /// field's NAME as the variable holding its value — which read plausibly and was wrong.
    /// </para>
    /// </summary>
    private void AddRead(XElement element)
    {
        string? entity = element.Attribute("EntityName")?.Value;
        string? attribute = element.Attribute("Attribute")?.Value;
        string? variable = element.Descendants().Where(child => child.Name.LocalName is "VisualBasicReference" or "OutArgument")
            .Select(child => VariableOf(child.Value))
            .FirstOrDefault(name => name is not null)
            ?? element.Attributes().Select(written => Bracketed(written.Value)).FirstOrDefault(name => name is not null);
        if (entity is not null && attribute is not null && variable is not null)
        {
            Reads[variable] = new AttributeRead(entity, attribute);
        }
    }

    /// <summary>
    /// Every output argument this element writes. Two shapes carry one: an <c>ActivityReference</c> keys them inside
    /// <c>.Arguments</c>, and a custom activity writes them as property elements of its own type. The first writer of
    /// a variable is kept: a later assignment to the same designer variable belongs to a different step.
    /// </summary>
    private void AddWriters(XElement element)
    {
        string? aqn = XamlNames.AssemblyQualifiedName(element);
        if (aqn is not null)
        {
            foreach (XElement argument in element.Elements().Where(child => child.Name.LocalName == "ActivityReference.Arguments")
                .SelectMany(child => child.Elements()).Where(child => child.Name.LocalName is "OutArgument" or "InOutArgument"))
            {
                Write(VariableOf(argument.Value), XamlNames.ShortTypeName(aqn), XamlNames.Key(argument) ?? "");
            }
            return;
        }
        foreach (XElement property in element.Elements().Where(XamlNames.IsPropertyElement))
        {
            string local = property.Name.LocalName;
            string name = local[(local.IndexOf('.', StringComparison.Ordinal) + 1)..];
            foreach (XElement written in property.Descendants().Where(child => child.Name.LocalName is "OutArgument" or "InOutArgument"))
            {
                Write(VariableOf(written.Value), element.Name.LocalName, name);
            }
        }
    }

    private void Write(string? variable, string activity, string argument)
    {
        if (variable is not null)
        {
            Writers.TryAdd(variable, new VariableSource(activity, argument));
        }
    }

    private void AddExpression(XElement element)
    {
        string? result = VariableOf(ArgumentText(element, "Result"));
        if (result is null)
        {
            return;
        }
        string operation = ArgumentText(element, "ExpressionOperator")?.Trim() ?? "";
        string parameters = ArgumentText(element, "Parameters") ?? "";
        if (operation == "CreateCrmType")
        {
            // { WorkflowPropertyType.OptionSetValue, "100000003", "Picklist" } — the first quoted string is the value.
            IReadOnlyList<string> quoted = QuotedStrings(parameters);
            if (quoted.Count > 1 && parameters.Contains("WorkflowPropertyType.EntityReference", StringComparison.Ordinal))
            {
                // A LOOKUP is written differently, and reading it like the rest reported the wrong thing entirely:
                // { WorkflowPropertyType.EntityReference, "ps_activitytype", "INBOUND - GELEN ARAMA", <id>, "Lookup" }
                // puts the TARGET TABLE first and the record second, so a condition against a lookup came out as
                // "NotEqual ps_activitytype" — the name of a table, which the workflow never compares anything to.
                Literals[result] = [quoted[1]];
                return;
            }
            Literals[result] = quoted.Count > 0 ? [quoted[0]] : [Dynamic];
            return;
        }
        Literals[result] = [Dynamic];
    }

    private void AddConversion(XElement element)
    {
        string? result = VariableOf(ArgumentText(element, "Result"));
        string? source = VariableOf(ArgumentText(element, "Value"));
        if (result is not null && source is not null)
        {
            _conversions[result] = source;
        }
    }

    /// <summary>A converted variable holds what its source holds. Chains of conversions are followed; a cycle stops.</summary>
    private void ResolveConversions()
    {
        foreach ((string result, string source) in _conversions)
        {
            string current = source;
            HashSet<string> seen = new(StringComparer.Ordinal) { result };
            while (seen.Add(current) && _conversions.TryGetValue(current, out string? next))
            {
                current = next;
            }
            if (Literals.TryGetValue(current, out IReadOnlyList<string>? values))
            {
                Literals.TryAdd(result, values);
            }
            if (Reads.TryGetValue(current, out AttributeRead? read))
            {
                Reads.TryAdd(result, read);
            }
            if (Writers.TryGetValue(current, out VariableSource? writer))
            {
                Writers[result] = writer;
            }
        }
    }

    private void AddComparison(XElement element)
    {
        string? result = VariableOf(ArgumentText(element, "Result"));
        string? operand = VariableOf(ArgumentText(element, "Operand"));
        if (result is not null && operand is not null)
        {
            Comparisons[result] = new Comparison(operand, ArgumentText(element, "ConditionOperator")?.Trim() ?? "", VariablesIn(ArgumentText(element, "Parameters")));
        }
    }

    private void AddLogical(XElement element)
    {
        string? result = VariableOf(ArgumentText(element, "Result"));
        string? left = VariableOf(ArgumentText(element, "LeftOperand"));
        string? right = VariableOf(ArgumentText(element, "RightOperand"));
        if (result is not null && left is not null && right is not null)
        {
            Logicals[result] = new LogicalCombination(ArgumentText(element, "LogicalOperator")?.Trim() ?? "And", left, right);
        }
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"\b[A-Za-z]+Step\d+_[A-Za-z0-9_]+\b")]
    private static partial Regex StepVariable();

    [GeneratedRegex("\"((?:[^\"]|\"\")*)\"")]
    private static partial Regex Quoted();
}
