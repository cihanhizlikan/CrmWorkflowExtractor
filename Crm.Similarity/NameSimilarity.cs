using System.Text;
using Crm.Ir.Text;

namespace Crm.Similarity;

/// <summary>§7.2 building blocks: Turkish-folded name tokens, common token prefix, Jaccard and Jaro–Winkler.</summary>
public static class NameSimilarity
{
    /// <summary>Splits on whitespace, punctuation, camel-case and letter/digit boundaries, then folds each token.</summary>
    public static IReadOnlyList<string> Tokens(string name)
    {
        List<string> tokens = [];
        StringBuilder current = new();
        char previous = '\0';
        foreach (char character in name)
        {
            bool boundary = !char.IsLetterOrDigit(character)
                || (current.Length > 0 && char.IsUpper(character) && char.IsLower(previous))
                || (current.Length > 0 && char.IsDigit(character) != char.IsDigit(previous));
            if (boundary && current.Length > 0)
            {
                tokens.Add(TurkishFold.Fold(current.ToString()));
                current.Clear();
            }
            if (char.IsLetterOrDigit(character))
            {
                current.Append(character);
            }
            previous = character;
        }
        if (current.Length > 0)
        {
            tokens.Add(TurkishFold.Fold(current.ToString()));
        }
        return tokens;
    }

    /// <summary>Leading tokens in common, over the shorter name's token count — the copy-paste-per-product detector.</summary>
    public static double CommonPrefix(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        int shorter = Math.Min(left.Count, right.Count);
        if (shorter == 0)
        {
            return 0;
        }
        int common = 0;
        while (common < shorter && string.Equals(left[common], right[common], StringComparison.Ordinal))
        {
            common++;
        }
        return (double)common / shorter;
    }

    /// <summary>|A ∩ B| / |A ∪ B|; two empty sets score 0, because two things with nothing in them are not similar.</summary>
    public static double Jaccard(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        if (left.Count == 0 && right.Count == 0)
        {
            return 0;
        }
        int intersection = left.Count(right.Contains);
        return (double)intersection / (left.Count + right.Count - intersection);
    }

    /// <summary>Jaro–Winkler with the standard 0.1 prefix scale over at most four characters. Rewards shared prefixes, deliberately.</summary>
    public static double JaroWinkler(string left, string right)
    {
        if (left.Length == 0 && right.Length == 0)
        {
            return 0;
        }
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return 1;
        }
        int window = Math.Max(0, (Math.Max(left.Length, right.Length) / 2) - 1);
        bool[] leftMatched = new bool[left.Length];
        bool[] rightMatched = new bool[right.Length];
        int matches = 0;
        for (int i = 0; i < left.Length; i++)
        {
            int from = Math.Max(0, i - window);
            int to = Math.Min(right.Length - 1, i + window);
            for (int j = from; j <= to; j++)
            {
                if (!rightMatched[j] && left[i] == right[j])
                {
                    leftMatched[i] = true;
                    rightMatched[j] = true;
                    matches++;
                    break;
                }
            }
        }
        if (matches == 0)
        {
            return 0;
        }

        int transpositions = 0;
        int k = 0;
        for (int i = 0; i < left.Length; i++)
        {
            if (!leftMatched[i])
            {
                continue;
            }
            while (!rightMatched[k])
            {
                k++;
            }
            if (left[i] != right[k])
            {
                transpositions++;
            }
            k++;
        }
        double m = matches;
        double jaro = ((m / left.Length) + (m / right.Length) + ((m - (transpositions / 2.0)) / m)) / 3.0;

        int prefix = 0;
        while (prefix < Math.Min(4, Math.Min(left.Length, right.Length)) && left[prefix] == right[prefix])
        {
            prefix++;
        }
        return jaro + (prefix * 0.1 * (1 - jaro));
    }
}
