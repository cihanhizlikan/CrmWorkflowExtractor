using Crm.Ir.Model;
using Crm.Ir.Text;

namespace Crm.Similarity;

/// <summary>
/// §7.1 step 1: a step reduced to its kind and shape with every literal removed. Entity and field names stay; values,
/// GUIDs, record names and numbers go. Updating <c>new_policy.new_status</c> to 100000003 and to 100000007 give the
/// same token.
/// </summary>
public static class StepSignature
{
    public static string Token(StepNode step)
    {
        string entity = Fold(step.Entity);
        return step.Kind switch
        {
            StepKind.CreateRecord or StepKind.UpdateRecord or StepKind.AssignRecord or StepKind.ChangeStatus
                => $"{step.Kind}({entity}:{string.Join(",", step.Fields.Select(field => Fold(field.Field)).Order(StringComparer.Ordinal))})",
            StepKind.CustomActivity => $"CustomActivity({TypeWithoutAssemblyVersion(step.Detail)})",
            StepKind.StopWorkflow => $"Stop({step.Detail})",
            StepKind.FormAction => $"FormAction({step.Detail}:{entity})",
            StepKind.Condition or StepKind.WaitCondition or StepKind.Variant => $"{step.Kind}[{step.Branches.Count}]",
            StepKind.Unmapped => $"Unmapped({step.Construct})",
            _ => step.Kind.ToString()
        };
    }

    /// <summary>A branch reduced to what it tests, without the tested values.</summary>
    public static string BranchToken(Branch branch)
    {
        if (branch.Predicate is not Predicate predicate)
        {
            return branch.Label is "Otherwise" or "Timeout" ? $"Branch({branch.Label})" : "Branch(?)";
        }
        return $"Branch({Fold(predicate.Entity)}.{Fold(predicate.Attribute)} {predicate.Operator})";
    }

    /// <summary>
    /// §7.1 step 2 input: every execution path from the first step to an end, as token sequences. A condition forks
    /// the path once per branch, and each fork continues with the steps after the condition.
    /// </summary>
    public static IReadOnlyList<string> Paths(IReadOnlyList<StepNode> steps, int maxPaths)
    {
        List<List<string>> paths = [[]];
        Extend(paths, Flatten(steps), maxPaths);
        return [.. paths.Select(path => string.Join(" > ", path)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>§7.1 step 3 input: the tree linearized depth-first, branch markers included.</summary>
    public static IReadOnlyList<string> Linear(IReadOnlyList<StepNode> steps)
    {
        List<string> tokens = [];
        foreach (StepNode step in Flatten(steps))
        {
            tokens.Add(Token(step));
            if (IsSplit(step))
            {
                foreach (Branch branch in step.Branches)
                {
                    tokens.Add(BranchToken(branch));
                    tokens.AddRange(Linear(branch.Steps));
                }
                tokens.Add("EndSplit");
            }
        }
        return tokens;
    }

    public static IReadOnlySet<string> Shingles(IReadOnlyList<string> tokens)
    {
        HashSet<string> shingles = new(StringComparer.Ordinal);
        for (int size = 2; size <= 3; size++)
        {
            for (int start = 0; start + size <= tokens.Count; start++)
            {
                shingles.Add(string.Join(" ", tokens.Skip(start).Take(size)));
            }
        }
        if (tokens.Count == 1)
        {
            shingles.Add(tokens[0]);
        }
        return shingles;
    }

    private static void Extend(List<List<string>> paths, IReadOnlyList<StepNode> steps, int maxPaths)
    {
        foreach (StepNode step in steps)
        {
            foreach (List<string> path in paths)
            {
                path.Add(Token(step));
            }
            if (!IsSplit(step) || step.Branches.Count == 0)
            {
                continue;
            }
            List<List<string>> forked = [];
            foreach (Branch branch in step.Branches)
            {
                List<List<string>> copies = [.. paths.Select(path => new List<string>(path) { BranchToken(branch) })];
                Extend(copies, Flatten(branch.Steps), maxPaths);
                forked.AddRange(copies);
                if (forked.Count >= maxPaths)
                {
                    break;
                }
            }
            paths.Clear();
            paths.AddRange(forked.Take(maxPaths));
        }
    }

    /// <summary>A plain Sequence node is grouping only; its children stand in its place.</summary>
    public static List<StepNode> Flatten(IReadOnlyList<StepNode> steps)
    {
        List<StepNode> flat = [];
        foreach (StepNode step in steps)
        {
            if (step.Kind == StepKind.Sequence)
            {
                flat.AddRange(Flatten([.. step.Branches.SelectMany(branch => branch.Steps)]));
                continue;
            }
            flat.Add(step);
        }
        return flat;
    }

    private static bool IsSplit(StepNode step)
    {
        return step.Kind is StepKind.Condition or StepKind.WaitCondition or StepKind.Variant;
    }

    private static string Fold(string? text)
    {
        return text is null ? "" : TurkishFold.Fold(text);
    }

    /// <summary><c>Partner.X, Partner, Version=1.2.0.0, …</c> → <c>Partner.X, Partner</c>: a rebuilt assembly is the same activity.</summary>
    private static string TypeWithoutAssemblyVersion(string? assemblyQualifiedName)
    {
        if (assemblyQualifiedName is null)
        {
            return "";
        }
        string[] parts = assemblyQualifiedName.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? $"{parts[0]}, {parts[1]}" : parts[0];
    }
}
