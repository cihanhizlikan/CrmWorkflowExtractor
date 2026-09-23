using System.Text;
using System.Text.RegularExpressions;
using Crm.Extract.Metadata;
using Crm.Ir.Model;
using Crm.Ir.Parsing;
using Crm.Ir.Reports;

namespace Crm.Cli.Reports;

/// <summary>
/// One custom activity: every workflow that calls it, the names it is called with, and the addresses written
/// inside the assembly it comes from. The last of those is the only record anywhere of where the call goes.
/// </summary>
public sealed record ExternalDependency(string Activity, string Assembly, IReadOnlyList<string> Workflows,
    IReadOnlyList<string> Parameters, IReadOnlyList<string> Addresses, bool? Registered);

/// <summary>One address written into a workflow's definition, and the workflow that carries it.</summary>
public sealed record ExternalAddress(string Host, string Address, string Workflow);

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
        return Dependencies(documents, PluginRegistry.Empty);
    }

    public static IReadOnlyList<ExternalDependency> Dependencies(IReadOnlyList<WorkflowIr> documents, PluginRegistry plugins)
    {
        PluginIndex registered = new(plugins);
        Dictionary<string, List<string>> callers = new(StringComparer.Ordinal);
        Dictionary<string, List<string>> parameters = new(StringComparer.Ordinal);
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
                    parameters[type] = [];
                }
                if (!workflows.Contains(document.Identity.Name, StringComparer.Ordinal))
                {
                    workflows.Add(document.Identity.Name);
                }
                // The names the activity is called with, gathered across every call site: the closest thing to a
                // signature that CRM keeps. They name the operation behind the activity, which the endpoint — sitting
                // in the assembly, out of reach — never does.
                foreach (NamedArgument argument in step.Arguments)
                {
                    if (!parameters[type].Contains(argument.Name, StringComparer.Ordinal))
                    {
                        parameters[type].Add(argument.Name);
                    }
                }
            }
        }

        return
        [
            .. callers.Select(entry => new ExternalDependency(ShortName(entry.Key), AssemblyOf(entry.Key),
                    [.. entry.Value.Order(StringComparer.Ordinal)], [.. parameters[entry.Key].Order(StringComparer.Ordinal)],
                    registered.AddressesOf(entry.Key), registered.IsRegistered(entry.Key)))
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
                found.Add(new ExternalAddress(HostOf(address), address, workflow));
            }
        }
        return [.. found.DistinctBy(address => address.Address)];
    }

    public static Sheet BuildDependencies(IReadOnlyList<WorkflowIr> documents, PluginRegistry plugins)
    {
        Sheet sheet = new(SheetNames.ExternalDependencies, "etkinlik", "derlemedeki_adresler", "cagiran_is_akisi_sayisi",
            "parametreler", "cagiran_is_akislari", "derleme", "kayitli");
        foreach (ExternalDependency dependency in Dependencies(documents, plugins))
        {
            sheet.Row(dependency.Activity, Sheet.List(dependency.Addresses), dependency.Workflows.Count,
                Sheet.List(dependency.Parameters, 80), Sheet.List(dependency.Workflows), dependency.Assembly,
                dependency.Registered);
        }
        return sheet;
    }

    /// <summary>
    /// Every plug-in step CRM runs: code on a message, outside any workflow. They are not in the process inventory
    /// and never will be — they are not processes — but they are the other half of what this CRM reaches outside.
    /// </summary>
    public static Sheet BuildPlugins(PluginRegistry plugins)
    {
        Dictionary<Guid, PluginType> types = plugins.Types.ToDictionary(type => type.TypeId);
        Dictionary<Guid, PluginAssembly> assemblies = plugins.Assemblies.ToDictionary(assembly => assembly.AssemblyId);
        Sheet sheet = new(SheetNames.Plugins, "kod", "adim", "durum", "mod", "konfigurasyondaki_adresler", "derleme");
        foreach (PluginStep step in plugins.Steps.OrderBy(step => step.Name, StringComparer.Ordinal))
        {
            PluginType? type = step.TypeId is Guid id ? types.GetValueOrDefault(id) : null;
            PluginAssembly? assembly = type?.AssemblyId is Guid owner ? assemblies.GetValueOrDefault(owner) : null;
            sheet.Row(ShortName(type?.TypeName ?? ""), step.Name, step.State == 0 ? "etkin" : "devre dışı",
                step.Mode == 1 ? "eşzamansız" : "eşzamanlı",
                Sheet.List(AssemblyStrings.Addresses(Encoding.UTF8.GetBytes(step.Configuration ?? ""))),
                assembly?.Name ?? "");
        }
        return sheet;
    }

    /// <summary>The server first, so that sorting the sheet answers "which outside systems do we touch" at a glance.</summary>
    public static Sheet BuildAddresses(IReadOnlyList<ExternalAddress> addresses)
    {
        Sheet sheet = new(SheetNames.Addresses, "sunucu", "adres", "is_akisi");
        foreach (ExternalAddress address in addresses
            .OrderBy(address => address.Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(address => address.Address, StringComparer.OrdinalIgnoreCase)
            .ThenBy(address => address.Workflow, StringComparer.Ordinal))
        {
            sheet.Row(address.Host, address.Address, address.Workflow);
        }
        return sheet;
    }

    /// <summary>
    /// Every registered workflow activity that has an address behind it, by type name: what the diagrams need to
    /// put an endpoint on a step. Activities whose assembly holds no address are left out rather than listed empty.
    /// </summary>
    public static IReadOnlyDictionary<string, string> AddressesByActivity(PluginRegistry plugins)
    {
        Dictionary<Guid, PluginAssembly> assemblies = plugins.Assemblies.ToDictionary(assembly => assembly.AssemblyId);
        Dictionary<string, string> addresses = new(StringComparer.Ordinal);
        foreach (PluginType type in plugins.Types.Where(type => type.AssemblyId is not null))
        {
            if (assemblies.TryGetValue(type.AssemblyId!.Value, out PluginAssembly? assembly) && assembly.Addresses.Count > 0)
            {
                addresses[type.TypeName] = string.Join(" · ", assembly.Addresses);
            }
        }
        return addresses;
    }

    /// <summary>
    /// The registered types by name, so a workflow's activity can be matched to the assembly it comes from. The
    /// workflow carries an assembly-qualified name; CRM's registration carries the type name alone.
    /// </summary>
    private sealed class PluginIndex
    {
        private readonly Dictionary<string, PluginType> _types = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, PluginAssembly> _assemblies;

        public PluginIndex(PluginRegistry plugins)
        {
            _assemblies = plugins.Assemblies.ToDictionary(assembly => assembly.AssemblyId);
            foreach (PluginType type in plugins.Types)
            {
                _types[type.TypeName] = type;
            }
        }

        public IReadOnlyList<string> AddressesOf(string assemblyQualifiedName)
        {
            PluginType? type = _types.GetValueOrDefault(TypeNameOf(assemblyQualifiedName));
            return type?.AssemblyId is Guid id && _assemblies.TryGetValue(id, out PluginAssembly? assembly) ? assembly.Addresses : [];
        }

        /// <summary>Null when nothing was retrieved: "not registered" would be a claim the run cannot make.</summary>
        public bool? IsRegistered(string assemblyQualifiedName)
        {
            return _types.Count == 0 ? null : _types.ContainsKey(TypeNameOf(assemblyQualifiedName));
        }

        private static string TypeNameOf(string assemblyQualifiedName)
        {
            return assemblyQualifiedName.Split(',')[0].Trim();
        }
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
