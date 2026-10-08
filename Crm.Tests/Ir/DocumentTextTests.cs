using Crm.Ir.Text;
using Xunit;

namespace Crm.Tests.Ir;

/// <summary>
/// The characters a document may carry. XML has no escape for a control character, so a writer handed one does
/// not produce a bad file — it throws, and the diagram, the workbook or the guide is simply never written.
/// </summary>
public sealed class DocumentTextTests
{
    [Theory]
    // What CRM's JSON and a plug-in's bytes can hold and XML cannot.
    [InlineData("Poli\u0001çe", "Poliçe")]
    [InlineData("a\u0003b", "ab")]
    [InlineData("\u001fson", "son")]
    // Legal in XML, invisible to every reader.
    [InlineData("a\u0085b", "ab")]
    [InlineData("a\u009fb", "ab")]
    // Not characters at all.
    [InlineData("a\ufffeb", "ab")]
    [InlineData("a\uffffb", "ab")]
    // Text, and kept.
    [InlineData("iki\tsütun", "iki\tsütun")]
    [InlineData("bir\nsatır", "bir\nsatır")]
    [InlineData("bir\r\nsatır", "bir\r\nsatır")]
    public void What_No_Document_May_Carry_Does_Not_Survive(string written, string kept)
    {
        Assert.Equal(kept, DocumentText.Writable(written));
    }

    /// <summary>
    /// Half a character, which is the one thing <c>XmlWriter</c> cannot be handed at all — and which
    /// <c>XmlConvert.IsXmlChar</c> says yes to, because inside a pair a high half IS character data.
    ///
    /// <para>
    /// A Fact and not a Theory because xUnit cannot carry a lone surrogate through <c>InlineData</c>: it arrives
    /// as the replacement character, the assertion then compares two legal strings, and the test proves nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void Half_A_Character_Is_Not_One()
    {
        Assert.Equal("ab", DocumentText.Writable("a\ud800b"));
        Assert.Equal("ab", DocumentText.Writable("a\udc00b"));
        Assert.Equal("a", DocumentText.Writable("a\ud800"));
        Assert.Equal("ab", DocumentText.Writable("a\udc00\ud800b"));
    }

    /// <summary>
    /// A surrogate PAIR is one character written as two <c>char</c>s, and dropping half of it would leave behind
    /// exactly the thing that cannot be written.
    /// </summary>
    [Fact]
    public void A_Character_Written_As_Two_Is_Kept_As_One()
    {
        Assert.Equal("a\U0001F600b", DocumentText.Writable("a\U0001F600b"));
        Assert.Equal("a\U0001F600b", DocumentText.Writable("a\U0001F600\u0001b"));
        Assert.Equal("\U00010000", DocumentText.Writable("\U00010000"));
    }

    [Fact]
    public void Turkish_Is_Text_And_Comes_Back_Whole()
    {
        string text = "Poliçe İptal — ğışİĞŞÇÖÜ «alıntı» 1–2 №";

        Assert.Same(text, DocumentText.Writable(text));
    }

    /// <summary>Nearly every string in the estate has nothing to lose; those come back as the same instance.</summary>
    [Fact]
    public void Text_With_Nothing_To_Lose_Is_Not_Rebuilt()
    {
        string text = "UpdateStep1: Müşteriyi güncelle";

        Assert.Same(text, DocumentText.Writable(text));
        Assert.NotSame(text, DocumentText.Writable(text + "\u0001"));
    }
}
