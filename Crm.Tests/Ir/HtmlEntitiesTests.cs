using Crm.Ir.Text;
using Xunit;

namespace Crm.Tests.Ir;

/// <summary>
/// The references CRM's designer leaves in the text it stores. A label came off the real data reading
/// "&amp;#160" where a space belonged, so these say what each shape resolves to and — as much to the point —
/// which shapes are left exactly as they arrived.
/// </summary>
public sealed class HtmlEntitiesTests
{
    [Theory]
    // The reported fault: a numeric reference with no semicolon, which every browser reads and no parser must.
    [InlineData("Müşteri&#160Adı", "Müşteri Adı")]
    [InlineData("Müşteri&#160;Adı", "Müşteri Adı")]
    [InlineData("Müşteri&nbsp;Adı", "Müşteri Adı")]
    [InlineData("Müşteri&#xA0;Adı", "Müşteri Adı")]
    [InlineData("Müşteri&#XA0;Adı", "Müşteri Adı")]
    // XML's own five, which arrive when the designer's text was escaped twice over.
    [InlineData("Fiyat &amp; Koşul", "Fiyat & Koşul")]
    [InlineData("&lt;boş&gt;", "<boş>")]
    [InlineData("&quot;iptal&quot;", "\"iptal\"")]
    [InlineData("it&apos;s", "it's")]
    // Punctuation a browser inserts when text is pasted into the designer.
    [InlineData("1&ndash;2", "1–2")]
    [InlineData("bitti&hellip;", "bitti…")]
    [InlineData("&bull; madde", "• madde")]
    // Turkish arrives as itself and must come back untouched.
    [InlineData("Poliçe İşlemi ğışİĞŞ", "Poliçe İşlemi ğışİĞŞ")]
    public void A_Reference_Becomes_The_Character_It_Stands_For(string written, string read)
    {
        Assert.Equal(read, HtmlEntities.Decode(written));
    }

    [Theory]
    // Nobody here knows these, and a guess on a diagram is worse than a visible oddity.
    [InlineData("&zwnj;")]
    [InlineData("&notareference;")]
    // A named reference without its semicolon is as likely to be someone's text as a dropped one.
    [InlineData("&nbspAdı")]
    [InlineData("A & B")]
    [InlineData("&")]
    [InlineData("&#")]
    [InlineData("&#;")]
    [InlineData("&#x;")]
    // Seven digits is not a code point anyone wrote on purpose.
    [InlineData("&#1234567;")]
    // Half a surrogate pair is not a character at all.
    [InlineData("&#xD800;")]
    [InlineData("&#0;")]
    public void What_Is_Not_A_Reference_This_Knows_Arrives_Verbatim(string written)
    {
        Assert.Equal(written, HtmlEntities.Decode(written));
    }

    /// <summary>
    /// ONE PASS. Text that really says "&amp;amp;" means an ampersand and the letters "amp;", and a second pass
    /// would hand the reader something CRM never held.
    /// </summary>
    [Fact]
    public void Decoding_Happens_Once_And_Not_Until_Nothing_Changes()
    {
        Assert.Equal("&amp;", HtmlEntities.Decode("&amp;amp;"));
        Assert.Equal("&#160;", HtmlEntities.Decode("&amp;#160;"));
    }

    /// <summary>A reference to a character no document may carry is dropped, as the writers drop a control character.</summary>
    [Fact]
    public void A_Reference_To_Something_No_Document_May_Carry_Is_Dropped()
    {
        Assert.Equal("ab", HtmlEntities.Decode("a&#3;b"));
        Assert.Equal("ab", HtmlEntities.Decode("a&#x1;b"));
        Assert.Equal("a\tb", HtmlEntities.Decode("a&#9;b"));
    }

    /// <summary>Nearly every string in the estate has no ampersand in it; those come back as the same instance.</summary>
    [Fact]
    public void Text_With_Nothing_To_Decode_Is_Not_Rebuilt()
    {
        string text = "UpdateStep1: Müşteriyi güncelle";

        Assert.Same(text, HtmlEntities.Decode(text));
    }
}
