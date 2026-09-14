namespace Crm.Tests;

/// <summary>Locates the repository root: the nearest ancestor of the test binary holding the solution file.</summary>
internal static class RepositoryTree
{
    private const string SolutionFile = "CrmWorkflowExtractor.slnx";

    public static DirectoryInfo Root()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, SolutionFile)))
            {
                return current;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException($"No {SolutionFile} above {AppContext.BaseDirectory}.");
    }
}
