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
        ("adres içinde kimlik bilgisi", CredentialUrl()),
        ("parola veya gizli anahtar", Secret()),
        ("bağlantı dizesi", ConnectionString()),
        ("kullanıcı adı", UserName()),
        ("gömülü adres", Url())
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
                    findings.Add(new SensitiveFinding(workflowId, workflowName, sourceFile, literal.Path.Length == 0 ? "kök" : literal.Path, category));
                    break;
                }
            }
        }
        return [.. findings.Distinct()];
    }

    public static string Markdown(IReadOnlyList<SensitiveFinding> findings)
    {
        StringBuilder text = new();
        text.AppendLine("# Hassas değerler — KISITLI").AppendLine();
        text.AppendLine("Bilgi güvenliği ekibi içindir. Değerler bilerek yazılmamıştır; görmek için burada adı geçen kaynak XAML dosyasını açın. "
            + "Bu dosyayı dağıtmayın.").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Bulgu: **{findings.Count}**; **{findings.Select(finding => finding.WorkflowId).Distinct().Count()}** iş akışında.").AppendLine();
        text.AppendLine("| Tür | İş akışı | Adım yolu | Kaynak dosya |").AppendLine("|---|---|---|---|");
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
