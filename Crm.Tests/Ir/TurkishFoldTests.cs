using Crm.Ir.Text;
using Xunit;

namespace Crm.Tests.Ir;

public sealed class TurkishFoldTests
{
    [Theory]
    [InlineData("POLİÇE İPTAL", "police iptal")]
    [InlineData("poliçe iptal", "police iptal")]
    [InlineData("Işık Şirketi Ğüzel Öğe", "isik sirketi guzel oge")]
    [InlineData("Kâr Payı", "kar payi")]
    [InlineData("hâlâ", "hala")]
    public void Turkish_Spellings_Fold_To_One_Key(string text, string expected)
    {
        Assert.Equal(expected, TurkishFold.Fold(text));
    }

    /// <summary>The case the Yalbuz table alone misses: capital I with a separately encoded combining dot.</summary>
    [Fact]
    public void A_Decomposed_Dotted_Capital_I_Folds_Like_The_Precomposed_One()
    {
        string decomposed = "İptal";

        Assert.Equal(TurkishFold.Fold("İptal"), TurkishFold.Fold(decomposed));
    }

    [Fact]
    public void Invariant_Lowercasing_Alone_Would_Split_These()
    {
        Assert.NotEqual("iptal", "İPTAL".ToLowerInvariant());
        Assert.Equal(TurkishFold.Fold("İPTAL"), TurkishFold.Fold("iptal"));
    }
}
