using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Crm.Ir.Model;
using Crm.Similarity;

namespace Crm.Consolidation;

/// <summary>A combined workflow, or the reason a family was not combined.</summary>
public sealed record CombineOutcome(string ClusterId, IReadOnlyList<Guid> Members, WorkflowIr? Combined, string? SkippedBecause, IReadOnlyList<string> ReconciliationErrors);

/// <summary>
/// Combines a family of similar workflows into one, as a set union of their behaviour (maintainer's decision
/// 2026-09-15, plan: <i>Consolidation design</i>).
///
/// <para>
/// <b>The algorithm is a prefix tree over step sequences.</b> Members are walked side by side. While every member's
/// next step has the same literal-stripped shape, those steps become ONE combined step that keeps every member's
/// values and sources. At the first position where members differ, a <see cref="StepKind.Variant"/> split is
/// emitted with one branch per group of members that still agree, and each branch continues the same way on its
/// own members. So a step shared by all is unconditional, a step shared by some sits behind a variant labelled with
/// exactly those members, and no member step is dropped or invented.
/// </para>
/// <para>
/// Known simplification, recorded in the plan: steps that members share AFTER a divergence are repeated in each
/// branch rather than merged back (a prefix union, not a full graph union).
/// </para>
/// </summary>
public static class WorkflowCombiner
{
    public static CombineOutcome Combine(WorkflowCluster cluster, IReadOnlyDictionary<Guid, WorkflowIr> documents)
    {
        List<WorkflowIr> members = [.. cluster.Members.Select(member => documents[member.WorkflowId]).OrderBy(document => document.Identity.WorkflowId)];
        List<Guid> ids = [.. members.Select(member => member.Identity.WorkflowId)];

        string? skip = SkipReason(cluster, members);
        if (skip is not null)
        {
            return new CombineOutcome(cluster.ClusterId, ids, null, skip, []);
        }

        Dictionary<Guid, string> names = members.ToDictionary(member => member.Identity.WorkflowId, member => member.Identity.Name);
        List<StepNode> steps = Merge([.. members.Select(member => new Lane(member.Identity.WorkflowId, StepSignature.Flatten(member.Steps)))], "", names);
        WorkflowIr medoid = documents[cluster.Medoid];
        WorkflowIr combined = new(
            medoid.Identity with { Name = $"Combined: {medoid.Identity.Name} (+{members.Count - 1})" },
            UnionTrigger(members),
            steps,
            new WorkflowDependencies(
                [.. members.SelectMany(member => member.Dependencies.ChildWorkflowCalls).Distinct().Order()],
                [.. members.SelectMany(member => member.Dependencies.CustomActivities).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]),
            new DataTouched(
                Union(members.Select(member => member.DataTouched.EntitiesRead)),
                Union(members.Select(member => member.DataTouched.EntitiesWritten)),
                Union(members.Select(member => member.DataTouched.FieldsRead)),
                Union(members.Select(member => member.DataTouched.FieldsWritten))),
            [.. members.SelectMany(member => member.Warnings.Select(warning => warning with { Message = $"[{member.Identity.Name}] {warning.Message}" }))],
            new IrProvenance("combined:" + cluster.ClusterId, MemberHash(members), medoid.Provenance.ExtractedAtUtc, medoid.Provenance.ToolVersion));

        return new CombineOutcome(cluster.ClusterId, ids, combined, null, Reconcile(members, combined));
    }

    /// <summary>
    /// The combined workflow must account for every step of every member exactly once. A union that lost or
    /// duplicated a step is not a union, and the run fails on it.
    /// </summary>
    public static IReadOnlyList<string> Reconcile(IReadOnlyList<WorkflowIr> members, WorkflowIr combined)
    {
        List<string> expected = [.. members.SelectMany(member => SourcesOf(member.Steps)).Select(Key).Order(StringComparer.Ordinal)];
        List<string> actual = [.. SourcesOf(combined.Steps).Select(Key).Order(StringComparer.Ordinal)];
        List<string> errors = [];
        foreach (IGrouping<string, string> missing in expected.GroupBy(key => key).Where(group => actual.Count(key => key == group.Key) < group.Count()))
        {
            errors.Add($"member step {missing.Key} is missing from the combined workflow");
        }
        foreach (IGrouping<string, string> extra in actual.GroupBy(key => key).Where(group => expected.Count(key => key == group.Key) < group.Count()))
        {
            errors.Add($"combined workflow accounts for {extra.Key} more times than the member has it");
        }
        return errors;
    }

    private static string? SkipReason(WorkflowCluster cluster, IReadOnlyList<WorkflowIr> members)
    {
        if (members.Count < 2)
        {
            return "a single workflow; nothing to combine";
        }
        if (cluster.LowCohesion)
        {
            return $"low cohesion (weakest internal score {cluster.MinimumInternalScore.ToString("0.00", CultureInfo.InvariantCulture)}): a chain rather than a family — split it first";
        }
        if (members.Select(member => member.Identity.PrimaryEntity ?? "").Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            return "members run on different primary entities: " + string.Join(", ", members.Select(member => member.Identity.PrimaryEntity ?? "none").Distinct(StringComparer.OrdinalIgnoreCase));
        }
        if (members.Select(member => member.Identity.Category).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            return "members are of different process categories: " + string.Join(", ", members.Select(member => member.Identity.Category).Distinct(StringComparer.Ordinal));
        }
        return null;
    }

    /// <summary>One member's remaining steps at the current depth of the merge.</summary>
    private sealed record Lane(Guid WorkflowId, IReadOnlyList<StepNode> Steps);

    private static List<StepNode> Merge(IReadOnlyList<Lane> lanes, string parentPath, IReadOnlyDictionary<Guid, string> names)
    {
        List<StepNode> result = [];
        for (int position = 0; ; position++)
        {
            string path = Child(parentPath, result.Count);
            List<IGrouping<string, Lane>> groups = [.. lanes
                .GroupBy(lane => position < lane.Steps.Count ? MergeKey(lane.Steps[position]) : "")
                .OrderBy(group => group.Key.Length == 0 ? 1 : 0)
                .ThenBy(group => group.Min(lane => lane.WorkflowId))];

            if (groups.Count == 1 && groups[0].Key.Length == 0)
            {
                return result;
            }
            if (groups.Count == 1)
            {
                result.Add(CombineStep([.. lanes.Select(lane => (lane.WorkflowId, lane.Steps[position]))], path, names));
                continue;
            }

            List<Branch> branches = [];
            foreach (IGrouping<string, Lane> group in groups)
            {
                List<Lane> rest = [.. group.Select(lane => new Lane(lane.WorkflowId, [.. lane.Steps.Skip(position)]))];
                List<Guid> memberIds = [.. group.Select(lane => lane.WorkflowId).Order()];
                string label = group.Key.Length == 0
                    ? "(ends here) " + MemberLabel(memberIds, names)
                    : MemberLabel(memberIds, names);
                branches.Add(new Branch(label, null, Merge(rest, Child(path, branches.Count), names), memberIds));
            }
            result.Add(new StepNode(path, StepKind.Variant, "Üyeler burada ayrışıyor", null, [], branches, null, [], "Variant", []));
            return result;
        }
    }

    private static StepNode CombineStep(IReadOnlyList<(Guid WorkflowId, StepNode Step)> steps, string path, IReadOnlyDictionary<Guid, string> names)
    {
        StepNode first = steps[0].Step;
        List<Branch> branches = [];
        for (int index = 0; index < first.Branches.Count; index++)
        {
            int branchIndex = index;
            List<Lane> lanes = [.. steps.Select(pair => new Lane(pair.WorkflowId, StepSignature.Flatten(pair.Step.Branches[branchIndex].Steps)))];
            Predicate? predicate = first.Branches[index].Predicate is Predicate shape
                ? shape with { Values = UnionValues(steps.Select(pair => (pair.WorkflowId, pair.Step.Branches[branchIndex].Predicate?.Values ?? []))) }
                : null;
            string label = Distinct(steps.Select(pair => pair.Step.Branches[branchIndex].Label), " | ");
            branches.Add(new Branch(label, predicate, Merge(lanes, Child(path, index), names), []));
        }

        List<FieldWrite> fields = [.. first.Fields.Select(field => new FieldWrite(field.Field,
            UnionValues(steps.Select(pair => (pair.WorkflowId, pair.Step.Fields.FirstOrDefault(other => other.Field == field.Field)?.Values ?? [])))))];
        List<NamedArgument> arguments = [.. steps.SelectMany(pair => pair.Step.Arguments).Distinct()
            .OrderBy(argument => argument.Name, StringComparer.Ordinal).ThenBy(argument => argument.Value, StringComparer.Ordinal)];
        string? detail = steps.All(pair => pair.Step.Detail is null) ? null : Distinct(steps.Select(pair => pair.Step.Detail ?? ""), " | ");

        return new StepNode(path, first.Kind, Distinct(steps.Select(pair => pair.Step.DisplayName), " | "), first.Entity, fields, branches, detail, arguments,
            first.Construct, [.. steps.SelectMany(pair => pair.Step.Sources)]);
    }

    /// <summary>Two steps merge when their shape matches and, for a split, every branch tests the same thing.</summary>
    private static string MergeKey(StepNode step)
    {
        string token = StepSignature.Token(step);
        return step.Branches.Count == 0 || step.Kind == StepKind.Sequence
            ? token
            : token + "{" + string.Join("|", step.Branches.Select(StepSignature.BranchToken)) + "}";
    }

    private static IReadOnlyList<LiteralValue> UnionValues(IEnumerable<(Guid WorkflowId, IReadOnlyList<LiteralValue> Values)> perMember)
    {
        return [.. perMember
            .SelectMany(pair => pair.Values.Select(value => (pair.WorkflowId, value)))
            .GroupBy(pair => (pair.value.Raw, pair.value.Resolved))
            .Select(group => new LiteralValue(group.Key.Raw, group.Key.Resolved) { Workflows = [.. group.Select(pair => pair.WorkflowId).Distinct().Order()] })
            .OrderBy(value => value.Raw, StringComparer.Ordinal)];
    }

    private static WorkflowTrigger UnionTrigger(IReadOnlyList<WorkflowIr> members)
    {
        return new WorkflowTrigger(
            members.Any(member => member.Trigger.OnCreate),
            members.Any(member => member.Trigger.OnDelete),
            Union(members.Select(member => member.Trigger.OnUpdateFields)),
            FirstOrJoined(members.Select(member => member.Trigger.CreateStage)),
            FirstOrJoined(members.Select(member => member.Trigger.UpdateStage)),
            FirstOrJoined(members.Select(member => member.Trigger.DeleteStage)),
            Distinct(members.Select(member => member.Trigger.RunAs), " | "),
            members.Any(member => member.Trigger.OnDemand));
    }

    private static IEnumerable<StepSource> SourcesOf(IReadOnlyList<StepNode> steps)
    {
        foreach (StepNode step in steps)
        {
            if (step.Kind is not (StepKind.Sequence or StepKind.Variant))
            {
                foreach (StepSource source in step.Sources)
                {
                    yield return source;
                }
            }
            foreach (StepSource source in step.Branches.SelectMany(branch => SourcesOf(branch.Steps)))
            {
                yield return source;
            }
        }
    }

    private static string Key(StepSource source)
    {
        return $"{source.WorkflowId:D}@{source.Path}";
    }

    private static string MemberLabel(IReadOnlyList<Guid> members, IReadOnlyDictionary<Guid, string> names)
    {
        return string.Join(", ", members.Select(member => names[member]));
    }

    private static string Child(string parent, int index)
    {
        string position = index.ToString(CultureInfo.InvariantCulture);
        return parent.Length == 0 ? position : $"{parent}/{position}";
    }

    private static IReadOnlyList<string> Union(IEnumerable<IReadOnlyList<string>> lists)
    {
        return [.. lists.SelectMany(list => list).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static string Distinct(IEnumerable<string> values, string separator)
    {
        return string.Join(separator, values.Where(value => value.Length > 0).Distinct(StringComparer.Ordinal));
    }

    private static string? FirstOrJoined(IEnumerable<string?> values)
    {
        List<string> distinct = [.. values.OfType<string>().Distinct(StringComparer.Ordinal)];
        return distinct.Count == 0 ? null : string.Join(" | ", distinct);
    }

    private static string MemberHash(IReadOnlyList<WorkflowIr> members)
    {
        string joined = string.Join("\n", members.Select(member => $"{member.Identity.WorkflowId:D}:{member.Provenance.SourceFileSha256}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
    }
}
