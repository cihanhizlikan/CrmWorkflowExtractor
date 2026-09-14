using System.Text.RegularExpressions;
using Xunit;

namespace Crm.Tests;

/// <summary>
/// Rules of <c>coding-style.md</c> that no analyzer can see, enforced by reading the source. Adopted from Yalbuz's
/// <c>RepositoryConventionTests</c>, including its worktree exclusion: <c>.claude/worktrees/</c> holds a complete
/// second copy of the tree under the root this walk starts from.
/// </summary>
public sealed partial class RepositoryConventionTests
{
    [Fact]
    public void No_Source_File_Ends_With_A_Blank_Line()
    {
        DirectoryInfo root = RepositoryTree.Root();
        List<FileInfo> sources = [.. SourceFiles(root)];
        Assert.NotEmpty(sources);

        List<string> offenders = [];
        foreach (FileInfo source in sources)
        {
            string text = File.ReadAllText(source.FullName);
            string trimmed = text.TrimEnd('\r', '\n');
            string tail = text[trimmed.Length..];
            if (text.Length > 0 && tail is not ("\n" or "\r\n"))
            {
                offenders.Add(Path.GetRelativePath(root.FullName, source.FullName));
            }
        }

        Assert.True(offenders.Count == 0,
            "coding-style.md rule 3 — a .cs file ends with exactly one newline:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Rule 4 and handout §10: a classic <c>Program</c> class with <c>static Main</c>, never top-level statements.</summary>
    [Fact]
    public void Every_Program_Is_A_Classic_Class_With_Static_Main()
    {
        DirectoryInfo root = RepositoryTree.Root();
        List<FileInfo> programs = [.. SourceFiles(root).Where(file => file.Name == "Program.cs")];
        Assert.NotEmpty(programs);

        List<string> offenders = [];
        foreach (FileInfo program in programs)
        {
            string text = File.ReadAllText(program.FullName);
            if (!ClassicProgram().IsMatch(text) || !StaticMain().IsMatch(text))
            {
                offenders.Add(Path.GetRelativePath(root.FullName, program.FullName));
            }
        }

        Assert.True(offenders.Count == 0,
            "coding-style.md rule 4 — Program must be a class with static Main:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<FileInfo> SourceFiles(DirectoryInfo root)
    {
        return root.EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file =>
            {
                string relative = Path.GetRelativePath(root.FullName, file.FullName);
                string[] segments = relative.Split(Path.DirectorySeparatorChar);
                return !segments.Contains("bin", StringComparer.Ordinal)
                    && !segments.Contains("obj", StringComparer.Ordinal)
                    && !relative.StartsWith($".claude{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
            });
    }

    [GeneratedRegex(@"^\s*(public |internal )?(static )?(sealed )?(partial )?class Program\b", RegexOptions.Multiline)]
    private static partial Regex ClassicProgram();

    [GeneratedRegex(@"\bstatic\s+(async\s+)?[A-Za-z<>]+\s+Main\s*\(")]
    private static partial Regex StaticMain();
}
