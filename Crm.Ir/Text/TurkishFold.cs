using System.Globalization;
using System.Text;

namespace Crm.Ir.Text;

/// <summary>
/// §5.2 comparison keys over Turkish text. <c>ToLowerInvariant()</c> alone is wrong here: it keeps <c>ı</c> and maps
/// <c>İ</c> to <c>i̇</c> (i + combining dot), so "İPTAL" and "iptal" would never meet. Fold ONLY for comparison;
/// the original string is always what is kept and displayed.
///
/// <para>
/// The explicit table is Yalbuz's <c>ScreenConceptBuilder.DiacriticFold</c>. Added here: canonical decomposition
/// with combining marks dropped, which covers the circumflexed vowels Turkish also uses (<c>kâr</c>, <c>hâlâ</c>)
/// and input that arrives already decomposed (<c>I</c> + U+0307) — both of which the table alone misses.
/// </para>
/// </summary>
public static class TurkishFold
{
    private static readonly Dictionary<char, char> Table = new()
    {
        ['ö'] = 'o',
        ['Ö'] = 'o',
        ['ü'] = 'u',
        ['Ü'] = 'u',
        ['ş'] = 's',
        ['Ş'] = 's',
        ['ç'] = 'c',
        ['Ç'] = 'c',
        ['ğ'] = 'g',
        ['Ğ'] = 'g',
        ['ı'] = 'i',
        ['İ'] = 'i'
    };

    public static string Fold(string text)
    {
        StringBuilder mapped = new(text.Length);
        foreach (char character in text)
        {
            mapped.Append(Table.TryGetValue(character, out char replacement) ? replacement : character);
        }

        string decomposed = mapped.ToString().Normalize(NormalizationForm.FormD);
        StringBuilder folded = new(decomposed.Length);
        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(char.ToLowerInvariant(character));
            }
        }
        return folded.ToString().Normalize(NormalizationForm.FormC);
    }
}
