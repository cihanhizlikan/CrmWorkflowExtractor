using System.Buffers.Binary;
using System.IO.Compression;
using Crm.Cli.Reports;
using Xunit;

namespace Crm.Tests.Cli;

/// <summary>
/// The PNG reader that puts the logo on the guide's cover. The part worth testing is the unfiltering: PNG stores
/// each row as a difference from its neighbours, so one wrong offset gives a picture that is plausible and wrong.
/// </summary>
public sealed class PngImageTests
{
    [Fact]
    public void Rows_Are_Unfiltered_Against_The_Pixel_Left_And_The_Row_Above()
    {
        // Row 0 is filtered against the pixel to its left (Sub), row 1 against the row above it (Up).
        byte[] file = Png(3, 2, 2, [1, 10, 20, 30, 5, 5, 5, 5, 5, 5, 2, 100, 100, 100, 100, 100, 100, 100, 100, 100]);

        PngImage image = Assert.IsType<PngImage>(PngImage.TryRead(file));

        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Null(image.Alpha);
        Assert.Equal([10, 20, 30, 15, 25, 35, 20, 30, 40], image.Rgb[..9]);
        Assert.Equal([110, 120, 130, 115, 125, 135, 120, 130, 140], image.Rgb[9..]);
    }

    [Fact]
    public void Transparency_Comes_Back_As_A_Mask_Of_Its_Own()
    {
        byte[] file = Png(2, 1, 6, [0, 10, 20, 30, 255, 40, 50, 60, 128]);

        PngImage image = Assert.IsType<PngImage>(PngImage.TryRead(file));

        Assert.Equal([10, 20, 30, 40, 50, 60], image.Rgb);
        Assert.Equal([255, 128], image.Alpha);
    }

    [Fact]
    public void A_File_That_Is_Not_A_Readable_Png_Is_Refused_Rather_Than_Guessed()
    {
        Assert.Null(PngImage.TryRead([1, 2, 3, 4]));
        Assert.Null(PngImage.TryRead(Png(2, 1, 2, [0, 1, 2, 3, 4, 5, 6], depth: 16)));
        Assert.Null(PngImage.TryRead(Png(2, 1, 2, [0, 1, 2, 3, 4, 5, 6], interlace: 1)));
    }

    /// <summary>
    /// The logo the tool carries. It is embedded rather than copied beside the executable because the host is
    /// locked down; if the build ever stops embedding it, the guide loses its cover mark silently.
    /// </summary>
    [Fact]
    public void The_Built_In_Logo_Is_Embedded_And_Reads_As_The_Mark()
    {
        using Stream? resource = typeof(AnalystGuide).Assembly.GetManifestResourceStream("Crm.Cli.Resources.kurumsal-logo.png");
        MemoryStream copy = new();
        Assert.NotNull(resource);
        resource.CopyTo(copy);

        PngImage logo = Assert.IsType<PngImage>(PngImage.TryRead(copy.ToArray()));

        Assert.Equal(436, logo.Width);
        Assert.Equal(94, logo.Height);
        Assert.Equal(436 * 94 * 3, logo.Rgb.Length);
        // The mark is drawn for a light ground and carries a blue and a green: both have to survive the palette.
        Assert.Contains(Pixels(logo), pixel => pixel.B > pixel.R + 60 && pixel.B > pixel.G + 40);
        Assert.Contains(Pixels(logo), pixel => pixel.G > pixel.R + 40 && pixel.G > pixel.B + 60);
    }

    private static IEnumerable<(byte R, byte G, byte B)> Pixels(PngImage image)
    {
        for (int at = 0; at + 2 < image.Rgb.Length; at += 3)
        {
            yield return (image.Rgb[at], image.Rgb[at + 1], image.Rgb[at + 2]);
        }
    }

    /// <summary>A PNG built by hand. The chunk checksums are left zero: the reader does not check them.</summary>
    private static byte[] Png(int width, int height, int colourType, byte[] rows, int depth = 8, int interlace = 0)
    {
        MemoryStream file = new();
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = (byte)depth;
        header[9] = (byte)colourType;
        header[12] = (byte)interlace;
        Chunk(file, "IHDR", header);

        MemoryStream deflated = new();
        using (ZLibStream stream = new(deflated, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            stream.Write(rows);
        }
        Chunk(file, "IDAT", deflated.ToArray());
        Chunk(file, "IEND", []);
        return file.ToArray();
    }

    private static void Chunk(Stream file, string tag, byte[] body)
    {
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, body.Length);
        file.Write(length);
        file.Write(System.Text.Encoding.ASCII.GetBytes(tag));
        file.Write(body);
        file.Write([0, 0, 0, 0]);
    }
}
