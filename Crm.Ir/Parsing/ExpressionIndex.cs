using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Crm.Ir.Parsing;

/// <summary>An attribute read into a variable by <c>GetEntityProperty</c>.</summary>
internal sealed record AttributeRead(string Entity, string Attribute);

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

    public Dictionary<string, IReadOnlyList<string>> Literals { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Comparison> Comparisons { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, LogicalCombination> Logicals { get; } = new(StringComparer.Ordinal);

    public static ExpressionIndex Build(XElement root)
    {
        ExpressionIndex index = new();
        foreach (XElement element in root.Descendants())
        {
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
        }
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
        return [.. Quoted().Matches(expression).Select(match => match.Groups[1].Value.Replace("\"\"", "\"", StringComparison.Ordinal))];
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

    private void AddRead(XElement element)
    {
        string? entity = element.Attribute("EntityName")?.Value;
        string? attribute = element.Attribute("Attribute")?.Value;
        string? variable = element.Descendants().Where(child => child.Name.LocalName is "VisualBasicReference" or "OutArgument")
            .Select(child => VariableOf(child.Value))
            .FirstOrDefault(name => name is not null);
        if (entity is not null && attribute is not null && variable is not null)
        {
            Reads[variable] = new AttributeRead(entity, attribute);
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
            Literals[result] = quoted.Count > 0 ? [quoted[0]] : [Dynamic];
            return;
        }
        Literals[result] = [Dynamic];
    }

    private void AddComparison(XElement element)
    {
        string? result = VariableOf(ArgumentText(element, "Result"));
        string? operand = VariableOf(ArgumentText(element, "Operand"));
        if (result is not null && operand is not null)
        {
            Comparisons[result] = new Comparison(operand, ArgumentText(element, "ConditionOperator")?.Trim() ?? "?", VariablesIn(ArgumentText(element, "Parameters")));
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
