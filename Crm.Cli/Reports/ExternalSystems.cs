using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Crm.Ir.Model;

namespace Crm.Cli.Reports;

/// <summary>One custom activity and every workflow that calls it.</summary>
public sealed record ExternalDependency(string Activity, string Assembly, IReadOnlyList<string> Workflows, IReadOnlyList<string> Addresses);

/// <summary>One address written into a workflow, and where it was found.</summary>
public sealed record ExternalAddress(string Address, string Workflow, string Step, string Argument);

/// <summary>
/// What the workflows reach outside CRM. A CRM workflow cannot call a service by itself: the only way is a custom
/// activity — compiled code registered in CRM — so every such call is one of these rows. What the activity does
/// inside its own assembly is NOT in the XAML; the name and the arguments are all CRM records, and that is what is
/// reported here. An address appears only when the workflow itself passes it; one hardcoded inside the assembly
/// cannot be seen from here at all.
/// </summary>
public static partial class ExternalSystems
{
    public static IReadOnlyList<ExternalDependency> Dependencies(IReadOnlyList<WorkflowIr> documents)
    {
        Dictionary<string, List<string>> callers = new(StringComparer.Ordinal);
        Dictionary<string, SortedSet<string>> addresses = new(StringComparer.Ordinal);
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
                    addresses[type] = [];
                }
                if (!workflows.Contains(document.Identity.Name, StringComparer.Ordinal))
                {
                    workflows.Add(document.Identity.Name);
                }
                foreach (NamedArgument argument in step.Arguments)
                {
                    foreach (Match match in Address().Matches(argument.Value))
                    {
                        addresses[type].Add(match.Value);
                    }
                }
            }
        }

        return
        [
            .. callers.Select(entry => new ExternalDependency(ShortName(entry.Key), AssemblyOf(entry.Key),
                    [.. entry.Value.Order(StringComparer.Ordinal)], [.. addresses[entry.Key]]))
                .OrderByDescending(dependency => dependency.Workflows.Count)
                .ThenBy(dependency => dependency.Activity, StringComparer.Ordinal)
        ];
    }

    /// <summary>Every address a workflow passes to a step, wherever it appears — custom activity or not.</summary>
    public static IReadOnlyList<ExternalAddress> Addresses(IReadOnlyList<WorkflowIr> documents)
    {
        List<ExternalAddress> found = [];
        foreach (WorkflowIr document in documents)
        {
            foreach (StepNode step in Walk(document.Steps))
            {
                foreach (NamedArgument argument in step.Arguments)
                {
                    foreach (Match match in Address().Matches(argument.Value))
                    {
                        found.Add(new ExternalAddress(match.Value, document.Identity.Name, step.DisplayName, argument.Name));
                    }
                }
            }
        }
        return [.. found.DistinctBy(address => (address.Address, address.Workflow, address.Argument))
            .OrderBy(address => address.Address, StringComparer.OrdinalIgnoreCase)
            .ThenBy(address => address.Workflow, StringComparer.Ordinal)];
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

    public static Sheet BuildAddresses(IReadOnlyList<WorkflowIr> documents)
    {
        Sheet sheet = new(SheetNames.Addresses, "adres", "is_akisi", "adim", "bagimsiz_degisken");
        foreach (ExternalAddress address in Addresses(documents))
        {
            sheet.Row(address.Address, address.Workflow, address.Step, address.Argument);
        }
        return sheet;
    }

    public static string Markdown(IReadOnlyList<WorkflowIr> documents)
    {
        IReadOnlyList<ExternalDependency> dependencies = Dependencies(documents);
        IReadOnlyList<ExternalAddress> addresses = Addresses(documents);

        StringBuilder text = new();
        text.AppendLine("# Dış sistemler").AppendLine();
        text.AppendLine("Bir CRM iş akışı bir servisi kendi başına çağıramaz: tek yol, CRM'e kaydedilmiş **özel bir etkinliktir** — derlenmiş koddur. Bu yüzden dışarıya uzanan her çağrı aşağıdaki satırlardan biridir.").AppendLine();
        text.AppendLine("**Görülebilen:** çağrılan etkinliğin adı, hangi iş akışlarının çağırdığı ve iş akışının ona geçirdiği bağımsız değişkenler — adres bunların arasındaysa adres de. **Görülemeyen:** etkinliğin kendi derlemesi içinde ne yaptığı; kodun içine gömülü bir adres buradan görünmez. Bunu yalnızca o derlemenin sahibi söyleyebilir.").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"- CRM dışına uzanan **{dependencies.Count} özel etkinlik**, toplam {dependencies.Sum(dependency => dependency.Workflows.Count)} çağrı");
        text.AppendLine(CultureInfo.InvariantCulture, $"- İş akışlarının geçirdiği **{addresses.Select(address => address.Address).Distinct(StringComparer.OrdinalIgnoreCase).Count()} farklı adres**").AppendLine();

        text.AppendLine("## En çok kullanılan özel etkinlikler").AppendLine();
        text.AppendLine("Yeni üründe her birinin karşılığının kurulması gerekir; üstte olanlar en çok iş akışını etkiler.").AppendLine();
        text.AppendLine("| Etkinlik | Derleme | Çağıran iş akışı |").AppendLine("|---|---|---:|");
        foreach (ExternalDependency dependency in dependencies.Take(40))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {dependency.Activity} | {dependency.Assembly} | {dependency.Workflows.Count} |");
        }
        if (dependencies.Count > 40)
        {
            text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"…ve `dis-sistemler.xlsx` kitabındaki {dependencies.Count - 40} etkinlik daha.");
        }
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Adreslerin tamamı kitabın **{SheetNames.Addresses}** sayfasındadır; üretim adresleri içerdiği için kurum dışına çıkarılmamalıdır.");
        return text.ToString();
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
}
