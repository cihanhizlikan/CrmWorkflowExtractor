using System.Globalization;
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
    /// <c>Poliçe İptal Süreci (ADMIN)</c> → <c>police-iptal-sureci-admin</c>. Always a valid file name on Windows
    /// and Linux: lower-case ASCII letters, digits and single hyphens.
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
        return slug.Length == 0 ? "workflow" : slug;
    }

    /// <summary>A cluster's file name: its medoid's name, marked as the combined form of a family.</summary>
    public static string ForFamily(string medoidName, string clusterId, int members)
    {
        string suffix = string.Create(CultureInfo.InvariantCulture, $"-combined-{members}");
        return Slug(medoidName) + suffix + "-" + clusterId[^8..];
    }
}
