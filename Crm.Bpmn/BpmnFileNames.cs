using System.Text;
using Crm.Ir.Text;

namespace Crm.Bpmn;

/// <summary>
/// File names an analyst can read: the workflow's own name, folded to ASCII (§5.3 Turkish fold) and hyphenated.
/// Two workflows may share a name, so a name that is not unique carries the start of its workflow id — the ids
/// themselves stay inside the file, in the process id and the provenance, which is what tooling follows.
/// </summary>
public static class BpmnFileNames
{
    public const int MaxSlugLength = 70;

    /// <summary>
    /// The names Windows keeps for devices, which a slug may land on exactly: a CRM entity whose logical name is
    /// <c>nul</c>, a workflow somebody called "AUX".
    ///
    /// <para>
    /// A path here is <c>bpmn/&lt;kategori&gt;/&lt;varlık&gt;/&lt;ad&gt;.bpmn</c>, so two of the three slugs become
    /// DIRECTORY names, with no extension — and a directory with one of these names cannot be created at all.
    /// Measured on this machine (Windows 11, 2026-10-08): <c>mkdir nul</c> throws, while <c>nul.bpmn</c> writes
    /// perfectly well as an ordinary file. The file half is therefore no longer a fault on Windows 11 — but it was
    /// one on every Windows before it, where that name opens the null device and the diagram is written to
    /// nothing with nobody told, and this tool runs on a locked-down host nobody here can test. So the name is
    /// moved off the reserved word in all three positions rather than in the two that are provably broken.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "con", "prn", "aux", "nul",
        "com0", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt0", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9"
    };

    /// <summary>One file name per workflow, in the order given; the mapping is stable for the same input.</summary>
    public static IReadOnlyDictionary<Guid, string> Assign(IEnumerable<(Guid Id, string Name)> workflows)
    {
        List<(Guid Id, string Name)> all = [.. workflows];
        Dictionary<string, int> slugCounts = new(StringComparer.Ordinal);
        foreach ((Guid _, string name) in all)
        {
            string slug = Slug(name);
            slugCounts[slug] = slugCounts.GetValueOrDefault(slug) + 1;
        }

        Dictionary<Guid, string> assigned = [];
        HashSet<string> taken = new(StringComparer.OrdinalIgnoreCase);
        foreach ((Guid id, string name) in all)
        {
            string slug = Slug(name);
            string candidate = slugCounts[slug] == 1 ? slug : $"{slug}-{id.ToString("N")[..8]}";
            if (!taken.Add(candidate))
            {
                // The same workflow id cannot repeat, so this only guards against a slug that collides with an
                // already-disambiguated one.
                candidate = $"{slug}-{id:N}";
                taken.Add(candidate);
            }
            assigned[id] = candidate;
        }
        return assigned;
    }

    /// <summary>
    /// <c>Poliçe İptal Süreci (ADMIN)</c> → <c>police-iptal-sureci-admin</c>. Always a valid file name AND folder
    /// name on Windows and Linux: lower-case ASCII letters, digits and single hyphens, never a name Windows keeps
    /// for a device. Having no character outside that set is also what rules out a trailing dot or space, which
    /// Windows silently strips — two names that differed only there would otherwise land on one file.
    /// </summary>
    public static string Slug(string name)
    {
        string folded = TurkishFold.Fold(name);
        StringBuilder text = new(folded.Length);
        foreach (char character in folded)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                text.Append(character);
            }
            else if (text.Length > 0 && text[^1] != '-')
            {
                text.Append('-');
            }
        }
        string slug = text.ToString().Trim('-');
        if (slug.Length > MaxSlugLength)
        {
            slug = slug[..MaxSlugLength].TrimEnd('-');
        }
        if (slug.Length == 0)
        {
            return "workflow";
        }
        // Only an EXACT match is a device. "console" and "com10" are ordinary names and are left alone.
        return ReservedNames.Contains(slug) ? slug + "-ayrilmis" : slug;
    }
}
