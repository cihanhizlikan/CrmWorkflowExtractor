using Crm.Extract.Runs;

namespace Crm.Extract.Xaml;

/// <summary>A XAML file from an earlier sealed run that can stand in for a fetch.</summary>
public sealed record ReusableXaml(XamlEntry Entry, string AbsolutePath, string RunId);

/// <summary>
/// §3.5 resumability across runs: when a sealed run already holds a workflow's XAML at the same
/// <c>versionnumber</c>, it is copied instead of refetched. Sealed runs only — an unsealed folder may hold a
/// half-written file. Newest run wins.
/// </summary>
public static class PriorRuns
{
    public static IReadOnlyDictionary<(Guid WorkflowId, long VersionNumber), ReusableXaml> FindReusableXaml(string outputRoot, string currentRunId)
    {
        Dictionary<(Guid, long), ReusableXaml> found = [];
        string runs = Path.Combine(outputRoot, "runs");
        if (!Directory.Exists(runs))
        {
            return found;
        }
        foreach (string runRoot in Directory.GetDirectories(runs).OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal))
        {
            string runId = Path.GetFileName(runRoot);
            if (string.Equals(runId, currentRunId, StringComparison.Ordinal) || !File.Exists(Path.Combine(runRoot, RunFolder.ManifestFileName)))
            {
                continue;
            }
            foreach (XamlEntry entry in XamlEntry.ReadIndex(runRoot))
            {
                string absolute = Path.Combine(runRoot, entry.File.Replace('/', Path.DirectorySeparatorChar));
                if (entry.VersionNumber is long version && File.Exists(absolute))
                {
                    found.TryAdd((entry.WorkflowId, version), new ReusableXaml(entry, absolute, runId));
                }
            }
        }
        return found;
    }
}
