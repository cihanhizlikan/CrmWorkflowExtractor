using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Crm.Ir.Parsing;

namespace Crm.Ir.Reports;

/// <summary>A literal that looks sensitive. The value itself is deliberately NOT kept (§5.3).</summary>
public sealed record SensitiveFinding(Guid WorkflowId, string WorkflowName, string SourceFile, string Path, string Category);

/// <summary>
/// §5.3: hardcoded URLs, credentials and connection strings in workflow literals — custom activity arguments above
/// all. The report names file, workflow, step path and category, and never the value: it is a finding for the
/// security team, not a document to circulate.
/// </summary>
public static partial class SensitiveLiteralScanner
{
    private static readonly (string Category, Regex Pattern)[] Rules =
    [
        ("credential in URL", CredentialUrl()),
        ("password or secret", Secret()),
        ("connection string", ConnectionString()),
        ("user name", UserName()),
        ("hardcoded URL", Url())
    ];

    public static IReadOnlyList<SensitiveFinding> Scan(Guid workflowId, string workflowName, string sourceFile, IReadOnlyList<XamlLiteral> literals)
    {
        List<SensitiveFinding> findings = [];
        foreach (XamlLiteral literal in literals)
        {
            if (IsSchemaNamespace(literal.Text))
            {
                continue;
            }
            foreach ((string category, Regex pattern) in Rules)
            {
                if (pattern.IsMatch(literal.Text))
                {
                    findings.Add(new SensitiveFinding(workflowId, workflowName, sourceFile, literal.Path.Length == 0 ? "root" : literal.Path, category));
                    break;
                }
            }
        }
        return [.. findings.Distinct()];
    }

    public static string Markdown(IReadOnlyList<SensitiveFinding> findings)
    {
        StringBuilder text = new();
        text.AppendLine("# Sensitive literals — RESTRICTED").AppendLine();
        text.AppendLine("For the security team. Values are redacted by design; open the source XAML named here to see one. "
            + "Do not circulate this file.").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Findings: **{findings.Count}** in **{findings.Select(finding => finding.WorkflowId).Distinct().Count()}** workflow(s).").AppendLine();
        text.AppendLine("| Category | Workflow | Step path | Source file |").AppendLine("|---|---|---|---|");
        foreach (SensitiveFinding finding in findings
            .OrderBy(finding => finding.Category, StringComparer.Ordinal)
            .ThenBy(finding => finding.WorkflowName, StringComparer.Ordinal)
            .ThenBy(finding => finding.Path, StringComparer.Ordinal))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {finding.Category} | {finding.WorkflowName} | {finding.Path} | `{finding.SourceFile}` |");
        }
        return text.ToString();
    }

    /// <summary>XAML is full of schema and clr namespace URIs; those are not findings.</summary>
    private static bool IsSchemaNamespace(string text)
    {
        return text.StartsWith("http://schemas.microsoft.com/", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("clr-namespace:", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"https?://[^/\s:@""]+:[^/\s@""]+@", RegexOptions.IgnoreCase)]
    private static partial Regex CredentialUrl();

    [GeneratedRegex(@"\b(password|passwd|pwd|secret|api[_-]?key|access[_-]?token|client[_-]?secret)\s*[=:]", RegexOptions.IgnoreCase)]
    private static partial Regex Secret();

    [GeneratedRegex(@"\b(data source|initial catalog|integrated security|server)\s*=[^;]+;", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionString();

    [GeneratedRegex(@"\b(user id|uid|username|user name)\s*[=:]", RegexOptions.IgnoreCase)]
    private static partial Regex UserName();

    [GeneratedRegex(@"\b(https?|ftp)://[^\s""]+", RegexOptions.IgnoreCase)]
    private static partial Regex Url();
}
