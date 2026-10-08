using System.Globalization;
using System.Text;

namespace Crm.Ir.Text;

/// <summary>
/// The characters a reference stands for, in text CRM's designer wrote.
///
/// <para>
/// A step's <c>DisplayName</c>, a condition's description and a literal argument are typed into a web designer,
/// which leaves HTML's own references behind in them: a label came out reading <c>&amp;#160</c> where a space
/// belonged. The XAML is read as XML, so one layer of escaping is already gone by the time the parser sees the
/// string — what is left is a reference CRM put IN the text, and only the reader is paying for it.
/// </para>
///
/// <para>
/// ONE PASS, never until nothing changes. Text that really does say <c>&amp;amp;</c> means an ampersand followed
/// by "amp;", and a second pass would turn it into something CRM never held. A reference nobody here knows is
/// left verbatim for the same reason — a guess on a diagram is worse than a visible oddity.
/// </para>
/// </summary>
public static class HtmlEntities
{
    /// <summary>
    /// The named references a CRM designer actually produces. XML's five, and the punctuation a browser inserts
    /// when text is pasted in. A non-breaking space becomes an ORDINARY space: it is a space to every reader, and
    /// keeping U+00A0 would leave two identical labels comparing unequal.
    /// </summary>
    private static readonly Dictionary<string, string> Named = new(StringComparer.Ordinal)
    {
        ["amp"] = "&",
        ["lt"] = "<",
        ["gt"] = ">",
        ["quot"] = "\"",
        ["apos"] = "'",
        ["nbsp"] = " ",
        ["ndash"] = "–",
        ["mdash"] = "—",
        ["hellip"] = "…",
        ["lsquo"] = "‘",
        ["rsquo"] = "’",
        ["ldquo"] = "“",
        ["rdquo"] = "”",
        ["bull"] = "•",
        ["middot"] = "·",
        ["laquo"] = "«",
        ["raquo"] = "»",
        ["deg"] = "°",
        ["times"] = "×",
        ["divide"] = "÷",
        ["euro"] = "€",
        ["pound"] = "£",
        ["copy"] = "©",
        ["reg"] = "®",
        ["trade"] = "™"
    };

    /// <summary>
    /// The text with its references resolved. A string with no <c>&amp;</c> in it is returned as it arrived, which
    /// is nearly every string in the estate.
    /// </summary>
    public static string Decode(string text)
    {
        if (!text.Contains('&', StringComparison.Ordinal))
        {
            return text;
        }

        StringBuilder decoded = new(text.Length);
        int at = 0;
        while (at < text.Length)
        {
            if (text[at] != '&')
            {
                decoded.Append(text[at]);
                at += 1;
                continue;
            }
            if (Reference(text, at) is not (string character, int length))
            {
                decoded.Append(text[at]);
                at += 1;
                continue;
            }
            decoded.Append(character);
            at += length;
        }
        return decoded.ToString();
    }

    /// <summary>
    /// What the reference at <paramref name="start"/> stands for and how long it is, or null when what follows the
    /// ampersand is not a reference this knows.
    /// </summary>
    private static (string Character, int Length)? Reference(string text, int start)
    {
        int at = start + 1;
        if (at < text.Length && text[at] == '#')
        {
            return Numeric(text, start);
        }

        int name = at;
        while (at < text.Length && char.IsAsciiLetterOrDigit(text[at]))
        {
            at += 1;
        }
        // A named reference needs its semicolon: "&nbspX" is as likely to be someone's text as a dropped one.
        if (at == name || at >= text.Length || text[at] != ';')
        {
            return null;
        }
        return Named.TryGetValue(text[name..at], out string? character) ? (character, at + 1 - start) : null;
    }

    /// <summary>
    /// A numeric reference, decimal or hexadecimal. The SEMICOLON IS OPTIONAL here, and that is the whole point of
    /// this package: what arrived on the real data was <c>&amp;#160</c> with nothing after it, which every browser
    /// reads as a space and a strict reader rejects. The digits end the reference by themselves.
    /// </summary>
    private static (string Character, int Length)? Numeric(string text, int start)
    {
        int at = start + 2;
        bool hex = at < text.Length && (text[at] == 'x' || text[at] == 'X');
        if (hex)
        {
            at += 1;
        }
        int digits = at;
        while (at < text.Length && (hex ? char.IsAsciiHexDigit(text[at]) : char.IsAsciiDigit(text[at])))
        {
            at += 1;
        }
        if (at == digits || at - digits > 6)
        {
            return null;
        }
        if (!int.TryParse(text[digits..at], hex ? NumberStyles.HexNumber : NumberStyles.None, CultureInfo.InvariantCulture, out int point))
        {
            return null;
        }
        int length = (at < text.Length && text[at] == ';' ? at + 1 : at) - start;
        if (point == 160)
        {
            return (" ", length);
        }
        // A reference to something no document may carry is not resolved into one: it is dropped, as the writers
        // drop a control character scanned out of a binary.
        if (point is 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF))
        {
            return null;
        }
        string character = char.ConvertFromUtf32(point);
        if (character.Length == 1 && char.IsControl(character[0]) && character[0] is not ('\t' or '\n' or '\r'))
        {
            return ("", length);
        }
        return (character, length);
    }
}
