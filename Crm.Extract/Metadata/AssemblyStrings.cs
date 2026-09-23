using System.Text;
using System.Text.RegularExpressions;

namespace Crm.Extract.Metadata;

/// <summary>
/// The addresses inside a registered assembly. A CRM workflow cannot call a service; a custom activity can, and
/// its endpoint is almost never passed in from the workflow — it is written in the code. That text is in the file
/// CRM itself stores, so the addresses can be read out of it without decompiling anything: a .NET assembly keeps
/// its string literals as UTF-16 in the metadata, with ASCII runs in the resources beside them, and an address is
/// recognisable in either.
///
/// <para>
/// What this can say is "the code this step runs contains these addresses", never "this step calls this address".
/// A literal built at run time from parts, or read from a configuration record, is not here at all. The assembly
/// bytes are scanned and dropped; nothing but the extracted text is kept, because a DLL on disk is a liability and
/// the addresses are the point.
/// </para>
/// </summary>
public static partial class AssemblyStrings
{
    /// <summary>A literal shorter than this is not an address anyone wrote deliberately.</summary>
    private const int Shortest = 12;

    public static IReadOnlyList<string> Addresses(byte[] assembly)
    {
        SortedSet<string> found = new(StringComparer.OrdinalIgnoreCase);
        foreach (string literal in Literals(assembly))
        {
            foreach (Match match in Address().Matches(literal))
            {
                string address = match.Value.TrimEnd('.', ',', ';', ')', '"', '\'');
                if (!Framework().IsMatch(address))
                {
                    found.Add(address);
                }
            }
        }
        return [.. found];
    }

    /// <summary>
    /// Printable runs, read twice: once as bytes and once as UTF-16, because a C# string constant is stored with a
    /// zero byte between every character and would otherwise read as a string of single letters.
    /// </summary>
    private static IEnumerable<string> Literals(byte[] assembly)
    {
        StringBuilder ascii = new();
        StringBuilder wide = new();
        for (int at = 0; at < assembly.Length; at++)
        {
            if (Printable(assembly[at]))
            {
                ascii.Append((char)assembly[at]);
            }
            else
            {
                if (ascii.Length >= Shortest)
                {
                    yield return ascii.ToString();
                }
                ascii.Clear();
            }

            if (at + 1 < assembly.Length && Printable(assembly[at]) && assembly[at + 1] == 0)
            {
                wide.Append((char)assembly[at]);
                at++;
                continue;
            }
            if (wide.Length >= Shortest)
            {
                yield return wide.ToString();
            }
            wide.Clear();
        }
        if (ascii.Length >= Shortest)
        {
            yield return ascii.ToString();
        }
        if (wide.Length >= Shortest)
        {
            yield return wide.ToString();
        }
    }

    private static bool Printable(byte value)
    {
        return value is >= 0x20 and <= 0x7E;
    }

    [GeneratedRegex(@"(https?|ftp|net\.tcp)://[^\s""'<>\\]+", RegexOptions.IgnoreCase)]
    private static partial Regex Address();

    /// <summary>Namespaces and schema URIs every .NET assembly carries; none of them is an endpoint.</summary>
    [GeneratedRegex(@"://(schemas\.|www\.w3\.org|www\.omg\.org|docs\.oasis|go\.microsoft\.com|schemas\.microsoft\.com|tempuri\.org/?$)", RegexOptions.IgnoreCase)]
    private static partial Regex Framework();
}
