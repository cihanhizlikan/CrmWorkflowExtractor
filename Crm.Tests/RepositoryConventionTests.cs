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

    /// <summary>
    /// The export must never build its document as one string. A browser refuses a string past about 512 million
    /// characters, and the TEST organisation passes that in XAML alone: on 2026-09-28 the export read for twenty
    /// minutes and died on its last line with "RangeError: Invalid string length", losing the whole run. The fix
    /// hands Blob an array of pieces, and the one call that would undo it is JSON.stringify over the whole
    /// document — which is what this looks for, because it is a one-word edit away from coming back.
    /// </summary>
    [Fact]
    public void The_Browser_Export_Never_Serializes_The_Whole_Document_As_One_String()
    {
        string script = File.ReadAllText(Path.Combine(RepositoryTree.Root().FullName, "tools", "crm-browser-export.js"));

        Assert.DoesNotContain("JSON.stringify(exported)", script, StringComparison.Ordinal);
        Assert.Contains("jsonPieces(exported", script, StringComparison.Ordinal);
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
