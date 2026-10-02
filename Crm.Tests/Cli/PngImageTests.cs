using Crm.Cli.Reports;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// The logo's size, which is all anyone needs now: a .docx carries the PNG file itself, and Word is told the box
/// to draw it in. Get the ratio wrong and the organisation's mark goes out stretched on the cover of every
/// package.
/// </summary>
public sealed class PngImageTests
{
    [Fact]
    public void The_Size_Comes_From_The_Header()
    {
        PngImage image = Assert.IsType<PngImage>(PngImage.TryRead(Png(436, 94)));

        Assert.Equal(436, image.Width);
        Assert.Equal(94, image.Height);
    }

    [Fact]
    public void Something_That_Is_Not_A_Png_Is_Refused_Rather_Than_Guessed()
    {
        Assert.Null(PngImage.TryRead([1, 2, 3, 4]));
        Assert.Null(PngImage.TryRead(new byte[40]));
        Assert.Null(PngImage.TryRead(Png(0, 10)));
    }

    /// <summary>
    /// The mark the cover carries, built into the tool so the locked-down host needs no loose file beside the exe.
    /// </summary>
    [Fact]
    public void The_Built_In_Logo_Is_Embedded_And_Reads_As_The_Mark()
    {
        using Stream? resource = typeof(PngImage).Assembly.GetManifestResourceStream("Crm.Cli.Resources.kurumsal-logo.png");
        Assert.NotNull(resource);
        using MemoryStream copy = new();
        resource.CopyTo(copy);

        PngImage logo = Assert.IsType<PngImage>(PngImage.TryRead(copy.ToArray()));

        Assert.Equal(436, logo.Width);
        Assert.Equal(94, logo.Height);
    }

    private static byte[] Png(int width, int height)
    {
        byte[] file = new byte[24];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(file, 0);
        "IHDR"u8.CopyTo(file.AsSpan(12));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(file.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(file.AsSpan(20), height);
        return file;
    }
}
