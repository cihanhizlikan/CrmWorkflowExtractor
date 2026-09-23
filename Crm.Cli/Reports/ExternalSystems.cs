using System.Text.RegularExpressions;
using Crm.Ir.Model;
using Crm.Ir.Parsing;
using Crm.Ir.Reports;

namespace Crm.Cli.Reports;

/// <summary>One custom activity and every workflow that calls it.</summary>
public sealed record ExternalDependency(string Activity, string Assembly, IReadOnlyList<string> Workflows);

/// <summary>One address written into a workflow's definition, and where in it the address was found.</summary>
public sealed record ExternalAddress(string Host, string Address, string Workflow, string Path);

/// <summary>
/// What the workflows reach outside CRM. A CRM workflow cannot call a service by itself: the only way is a custom
/// activity — compiled code registered in CRM — so every such call is one of these rows. What the activity does
/// inside its own assembly is NOT in the XAML; the name and the arguments are all CRM records, and that is what is
/// reported here. An address hardcoded inside the assembly cannot be seen from here at all.
///
/// <para>
/// Addresses are read from every literal in the definition, which is the same text the sensitive-literal scan
/// reads. An earlier version took them from the arguments captured on mapped steps, and on the real data that set
/// was empty while the scan was finding embedded addresses — an address in a variable, in an expression or inside
/// a construct the parser could not read never reached the page, and an empty page reads as "calls nothing".
/// </para>
/// </summary>
public static partial class ExternalSystems
{
    public static IReadOnlyList<ExternalDependency> Dependencies(IReadOnlyList<WorkflowIr> documents)
    {
        Dictionary<string, List<string>> callers = new(StringComparer.Ordinal);
        foreach (WorkflowIr document in documents)
        {
            foreach (StepNode step in Walk(document.Steps).Where(step => step.Kind == StepKind.CustomActivity))
            {
                string type = step.Detail ?? step.Construct ?? "";
                if (type.Length == 0)
                {
                    continue;
                }
                if (!callers.TryGetValue(type, out List<string>? workflows))
                {
                    workflows = [];
                    callers[type] = workflows;
                }
                if (!workflows.Contains(document.Identity.Name, StringComparer.Ordinal))
                {
                    workflows.Add(document.Identity.Name);
                }
            }
        }

        return
        [
            .. callers.Select(entry => new ExternalDependency(ShortName(entry.Key), AssemblyOf(entry.Key), [.. entry.Value.Order(StringComparer.Ordinal)]))
                .OrderByDescending(dependency => dependency.Workflows.Count)
                .ThenBy(dependency => dependency.Activity, StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// Every address in one workflow's definition. The XAML's own schema and clr namespaces are not addresses, and
    /// a user name and password written into one are masked: this page is delivered, the restricted report is not.
    /// </summary>
    public static IReadOnlyList<ExternalAddress> Find(string workflow, IReadOnlyList<XamlLiteral> literals)
    {
        List<ExternalAddress> found = [];
        foreach (XamlLiteral literal in literals.Where(literal => !SensitiveLiteralScanner.IsSchemaNamespace(literal.Text)))
        {
            foreach (Match match in Address().Matches(literal.Text))
            {
                string address = Mask(match.Value);
                found.Add(new ExternalAddress(HostOf(address), address, workflow, literal.Path.Length == 0 ? "kök" : literal.Path));
            }
        }
        return [.. found.DistinctBy(address => (address.Address, address.Path))];
    }

    public static Sheet BuildDependencies(IReadOnlyList<WorkflowIr> documents)
    {
        Sheet sheet = new(SheetNames.ExternalDependencies, "etkinlik", "cagiran_is_akisi_sayisi", "cagiran_is_akislari", "derleme");
        foreach (ExternalDependency dependency in Dependencies(documents))
        {
            sheet.Row(dependency.Activity, dependency.Workflows.Count, string.Join(" | ", dependency.Workflows), dependency.Assembly);
        }
        return sheet;
    }

    /// <summary>The server first, so that sorting the sheet answers "which outside systems do we touch" at a glance.</summary>
    public static Sheet BuildAddresses(IReadOnlyList<ExternalAddress> addresses)
    {
        Sheet sheet = new(SheetNames.Addresses, "sunucu", "adres", "is_akisi", "adim_yolu");
        foreach (ExternalAddress address in addresses
            .OrderBy(address => address.Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(address => address.Address, StringComparer.OrdinalIgnoreCase)
            .ThenBy(address => address.Workflow, StringComparer.Ordinal))
        {
            sheet.Row(address.Host, address.Address, address.Workflow, address.Path);
        }
        return sheet;
    }

    private static IEnumerable<StepNode> Walk(IReadOnlyList<StepNode> steps)
    {
        foreach (StepNode step in steps)
        {
            yield return step;
            foreach (StepNode child in step.Branches.SelectMany(branch => Walk(branch.Steps)))
            {
                yield return child;
            }
        }
    }

    /// <summary><c>https://user:secret@host/path</c> → <c>https://***@host/path</c>.</summary>
    private static string Mask(string address)
    {
        return Credentials().Replace(address, "$1://***@");
    }

    /// <summary>The server the address names: <c>https://nova.local:8443/api</c> → <c>nova.local</c>.</summary>
    private static string HostOf(string address)
    {
        if (address.StartsWith(@"\\", StringComparison.Ordinal))
        {
            string share = address[2..];
            int slash = share.IndexOf('\\', StringComparison.Ordinal);
            return slash < 0 ? share : share[..slash];
        }
        int scheme = address.IndexOf("://", StringComparison.Ordinal);
        string rest = scheme < 0 ? address : address[(scheme + 3)..];
        int at = rest.LastIndexOf('@');
        string host = at < 0 ? rest : rest[(at + 1)..];
        int end = host.IndexOfAny(['/', ':', '?', '#']);
        return end < 0 ? host : host[..end];
    }

    /// <summary><c>Anadolu.Crm.Activities.SendSignatureToNova, Anadolu.Crm, Version=…</c> → <c>SendSignatureToNova</c>.</summary>
    private static string ShortName(string assemblyQualifiedName)
    {
        string type = assemblyQualifiedName.Split(',')[0].Trim();
        int dot = type.LastIndexOf('.');
        return dot < 0 ? type : type[(dot + 1)..];
    }

    private static string AssemblyOf(string assemblyQualifiedName)
    {
        string[] parts = assemblyQualifiedName.Split(',');
        return parts.Length > 1 ? parts[1].Trim() : "";
    }

    /// <summary>An http(s) or ftp address, or a UNC share — the shapes an endpoint written into a workflow takes.</summary>
    [GeneratedRegex(@"(https?|ftp)://[^\s""'<>\]]+|\\\\[A-Za-z0-9._-]+\\[^\s""'<>\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex Address();

    [GeneratedRegex(@"(https?|ftp)://[^/\s:@""]+:[^/\s@""]*@", RegexOptions.IgnoreCase)]
    private static partial Regex Credentials();
}
