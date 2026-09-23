using Crm.Ir.Model;

namespace Crm.Cli.Reports;

/// <summary>
/// Who calls whom. A workflow nobody calls is an entry point — a process in its own right. One that others call is
/// a building block, and rebuilding it twice would be waste. Together a parent and everything below it is one
/// migration unit, which is the honest size of the work, not the 1437 files.
/// </summary>
public sealed class CallGraph
{
    public const string EntryPoint = "giriş noktası";
    public const string BuildingBlock = "yapı taşı";
    public const string Both = "giriş noktası + yapı taşı";

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

    /// <summary>One row per node of every process tree: the root, the depth, and the workflow at that depth.</summary>
    public Sheet BuildTrees()
    {
        // A tree of one node is not a tree: the plan already carries that workflow as a line of its own.
        Sheet sheet = new(SheetNames.Trees, "kok_is_akisi", "derinlik", "is_akisi", "rol");
        foreach (Guid root in Calls.Keys
            .Where(id => CalledBy.GetValueOrDefault(id, []).Count == 0 && Calls[id].Count > 0)
            .OrderBy(id => Names[id], StringComparer.Ordinal))
        {
            IReadOnlyList<(Guid Id, int Depth)> tree = Tree(root);
            if (tree.Count < 2)
            {
                continue;
            }
            foreach ((Guid id, int depth) in tree)
            {
                sheet.Row(Names[root], depth, Names.GetValueOrDefault(id, "(envanterde bulunamadı)"), RoleOf(id));
            }
        }
        return sheet;
    }

    public Sheet Build()
    {
        // Only the workflows that are part of a call: for everything else the plan's own rol column already says
        // "giriş noktası", and a row that repeats it is a row a reader has to skip.
        Sheet csv = new(SheetNames.CallGraph, "is_akisi", "rol", "alt_akislar", "cagiranlar", "is_akisi_id");
        foreach ((Guid id, IReadOnlyList<Guid> children) in Calls.OrderBy(entry => Names[entry.Key], StringComparer.Ordinal))
        {
            IReadOnlyList<Guid> parents = CalledBy.GetValueOrDefault(id, []);
            if (children.Count == 0 && parents.Count == 0)
            {
                continue;
            }
            csv.Row(Names[id], RoleOf(id),
                Sheet.List(children.Select(child => Names.GetValueOrDefault(child, "(envanterde bulunamadı)"))),
                Sheet.List(parents.Select(parent => Names[parent])), id);
        }
        return csv;
    }
}
