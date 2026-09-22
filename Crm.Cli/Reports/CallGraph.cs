using System.Globalization;
using System.Text;
using Crm.Ir.Model;

namespace Crm.Cli.Reports;

/// <summary>
/// Who calls whom. A workflow nobody calls is an entry point — a process in its own right. One that others call is
/// a building block, and rebuilding it twice would be waste. Together a parent and everything below it is one
/// migration unit, which is the honest size of the work, not the 1437 files.
/// </summary>
public sealed class CallGraph
{
    public const string EntryPoint = "entry point";
    public const string BuildingBlock = "building block";
    public const string Both = "entry point + building block";

    private CallGraph(IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> calls, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> calledBy,
        IReadOnlyDictionary<Guid, string> names, IReadOnlyList<Guid> missing)
    {
        Calls = calls;
        CalledBy = calledBy;
        Names = names;
        MissingTargets = missing;
    }

    public IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> Calls { get; }

    public IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> CalledBy { get; }

    public IReadOnlyDictionary<Guid, string> Names { get; }

    /// <summary>Child workflows called but not among the parsed definitions: deleted, or a kind this run did not parse.</summary>
    public IReadOnlyList<Guid> MissingTargets { get; }

    public static CallGraph Build(IReadOnlyList<WorkflowIr> documents)
    {
        Dictionary<Guid, string> names = documents.ToDictionary(document => document.Identity.WorkflowId, document => document.Identity.Name);
        Dictionary<Guid, IReadOnlyList<Guid>> calls = [];
        Dictionary<Guid, List<Guid>> calledBy = [];
        SortedSet<Guid> missing = [];
        foreach (WorkflowIr document in documents)
        {
            List<Guid> children = [.. document.Dependencies.ChildWorkflowCalls.Distinct().Order()];
            calls[document.Identity.WorkflowId] = children;
            foreach (Guid child in children)
            {
                if (!names.ContainsKey(child))
                {
                    missing.Add(child);
                    continue;
                }
                if (!calledBy.TryGetValue(child, out List<Guid>? parents))
                {
                    parents = [];
                    calledBy[child] = parents;
                }
                parents.Add(document.Identity.WorkflowId);
            }
        }
        return new CallGraph(calls, calledBy.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<Guid>)entry.Value), names, [.. missing]);
    }

    public string RoleOf(Guid workflowId)
    {
        bool called = CalledBy.GetValueOrDefault(workflowId, []).Count > 0;
        bool callsOthers = Calls.GetValueOrDefault(workflowId, []).Count > 0;
        return called && callsOthers ? Both : called ? BuildingBlock : EntryPoint;
    }

    /// <summary>Everything reachable from a workflow, depth first, following each branch once; a cycle stops at its repeat.</summary>
    public IReadOnlyList<(Guid Id, int Depth)> Tree(Guid root)
    {
        List<(Guid, int)> nodes = [];
        Walk(root, 0, [], nodes);
        return nodes;
    }

    private void Walk(Guid current, int depth, HashSet<Guid> seen, List<(Guid, int)> nodes)
    {
        nodes.Add((current, depth));
        if (!seen.Add(current))
        {
            return;
        }
        foreach (Guid child in Calls.GetValueOrDefault(current, []).Where(Names.ContainsKey))
        {
            Walk(child, depth + 1, seen, nodes);
        }
    }

    public string Markdown()
    {
        List<Guid> roots = [.. Calls.Keys.Where(id => CalledBy.GetValueOrDefault(id, []).Count == 0 && Calls[id].Count > 0)
            .OrderBy(id => Names[id], StringComparer.Ordinal)];
        StringBuilder text = new();
        text.AppendLine("# Call graph").AppendLine();
        text.AppendLine("A workflow nobody calls is a process of its own; one that others call is a building block shared between processes. A parent and everything under it is **one** migration unit.").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"- {Calls.Count} workflows · {CalledBy.Count} called by at least one other · {roots.Count} entry points that call children");
        text.AppendLine(CultureInfo.InvariantCulture, $"- {Calls.Values.Sum(children => children.Count)} child-workflow calls in total");
        if (MissingTargets.Count > 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- **{MissingTargets.Count} calls point at a workflow that is not in this run** (deleted, or not parsed). They are listed at the end.");
        }
        text.AppendLine();

        text.AppendLine("## Building blocks, most used first").AppendLine();
        text.AppendLine("| Workflow | Called by | Callers |").AppendLine("|---|---:|---|");
        foreach ((Guid id, IReadOnlyList<Guid> parents) in CalledBy.OrderByDescending(entry => entry.Value.Count).ThenBy(entry => Names[entry.Key], StringComparer.Ordinal).Take(40))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {Names[id]} | {parents.Count} | {string.Join(", ", parents.Take(6).Select(parent => Names[parent]))}{(parents.Count > 6 ? ", …" : "")} |");
        }
        text.AppendLine();

        text.AppendLine("## Process trees").AppendLine();
        text.AppendLine("Each tree is one migration unit. Indentation is the call depth.").AppendLine();
        foreach (Guid root in roots)
        {
            text.AppendLine("```");
            foreach ((Guid id, int depth) in Tree(root))
            {
                text.Append(new string(' ', depth * 2)).Append(depth == 0 ? "" : "└ ").AppendLine(Names.GetValueOrDefault(id, id.ToString("D")));
            }
            text.AppendLine("```");
        }
        if (MissingTargets.Count > 0)
        {
            text.AppendLine().AppendLine("## Calls to workflows not in this run").AppendLine();
            foreach (Guid id in MissingTargets)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- `{id:D}`");
            }
        }
        return text.ToString();
    }

    public byte[] Csv()
    {
        ExcelCsv csv = new("workflow_name", "workflow_id", "role", "calls", "called_by", "children", "callers");
        foreach ((Guid id, IReadOnlyList<Guid> children) in Calls.OrderBy(entry => Names[entry.Key], StringComparer.Ordinal))
        {
            IReadOnlyList<Guid> parents = CalledBy.GetValueOrDefault(id, []);
            csv.Row(Names[id], id, RoleOf(id), children.Count, parents.Count,
                string.Join(" | ", children.Select(child => Names.GetValueOrDefault(child, child.ToString("D")))),
                string.Join(" | ", parents.Select(parent => Names[parent])));
        }
        return csv.ToBytes();
    }
}
