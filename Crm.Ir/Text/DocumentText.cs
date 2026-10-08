using System.Text;
using System.Xml;

namespace Crm.Ir.Text;

/// <summary>
/// Text an XML document may actually carry.
///
/// <para>
/// XML 1.0 has no escape for a control character: there is no spelling of U+0001 that a document may hold, so a
/// writer handed one does not produce a bad file — it throws, and whatever was being written is lost. CRM's own
/// records are XML and cannot carry one either, but the names and values around them arrive as JSON, which can,
/// and the strings scanned out of a plug-in assembly are full of them.
/// </para>
///
/// <para>
/// Also dropped: a lone surrogate, which is half a character; U+FFFE and U+FFFF, which are not characters at all;
/// and the C1 range, which is legal in XML and still invisible to every reader. Tab, newline and carriage return
/// are text and are kept. The rule lives here because it belongs to all three writers — it was written twice and
/// missed in the third, which is how a diagram came to be unwritable.
/// </para>
/// </summary>
public static class DocumentText
{
    /// <summary>
    /// The text with nothing in it a document cannot hold. Text that has nothing to lose comes back as the same
    /// instance, which is nearly every string in the estate.
    /// </summary>
    public static string Writable(string text)
    {
        int at = 0;
        while (at < text.Length && Keep(text, at, out int width))
        {
            at += width;
        }
        if (at == text.Length)
        {
            return text;
        }

        StringBuilder kept = new(text.Length);
        kept.Append(text[..at]);
        while (at < text.Length)
        {
            if (Keep(text, at, out int width))
            {
                kept.Append(text, at, width);
                at += width;
                continue;
            }
            at += width;
        }
        return kept.ToString();
    }

    /// <summary>
    /// Whether the character at <paramref name="at"/> stays, and how many <c>char</c>s of the string it occupies —
    /// two for a surrogate pair, which is one character written as two and is kept or dropped as one thing.
    /// </summary>
    private static bool Keep(string text, int at, out int width)
    {
        char letter = text[at];
        if (char.IsHighSurrogate(letter) && at + 1 < text.Length && char.IsLowSurrogate(text[at + 1]))
        {
            width = 2;
            return true;
        }
        width = 1;
        if (letter is '\t' or '\n' or '\r')
        {
            return true;
        }
        // A surrogate that reached here is not in a pair, and half a character is the one thing XmlWriter cannot
        // be handed — IsXmlChar says yes to a high half, because in a pair it is character data. IsXmlChar then
        // refuses U+FFFE, U+FFFF and every C0 control; IsControl adds the C1 range, which XML permits and nobody
        // can read.
        return !char.IsSurrogate(letter) && XmlConvert.IsXmlChar(letter) && !char.IsControl(letter);
    }
}
