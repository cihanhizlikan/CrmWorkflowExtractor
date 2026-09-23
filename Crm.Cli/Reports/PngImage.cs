using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Crm.Cli.Reports;

/// <summary>
/// A PNG opened far enough to put it in a PDF: the pixels as RGB, and the transparency beside them.
///
/// <para>
/// PDF and PNG agree on how a picture is stored — both deflate rows of 8-bit samples — but they disagree on
/// everything around it: PNG filters each row against the one above, keeps a palette, and carries transparency in
/// the same stream as the colour, where a PDF wants it as a separate grey image. So the file is unpacked here and
/// handed on in the two pieces a PDF understands.
/// </para>
/// </summary>
public sealed class PngImage
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    private PngImage(int width, int height, byte[] rgb, byte[]? alpha)
    {
        Width = width;
        Height = height;
        Rgb = rgb;
        Alpha = alpha;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Three bytes per pixel, row by row from the top.</summary>
    public byte[] Rgb { get; }

    /// <summary>One byte per pixel, or null when every pixel is opaque and the PDF needs no soft mask.</summary>
    public byte[]? Alpha { get; }

    /// <summary>
    /// The picture, or null when the file is not one this can read. Eight bits a sample and no interlacing, which
    /// is what every logo anyone exports is; anything else is refused rather than drawn wrong.
    /// </summary>
    public static PngImage? TryRead(byte[] file)
    {
        try
        {
            return Read(file);
        }
        catch (Exception error) when (error is IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException or InvalidDataException)
        {
            return null;
        }
    }

    private static PngImage? Read(byte[] file)
    {
        if (file.Length < 8 || !file.AsSpan(0, 8).SequenceEqual(Signature))
        {
            return null;
        }

        int width = 0;
        int height = 0;
        int colourType = 0;
        byte[] palette = [];
        byte[] paletteAlpha = [];
        MemoryStream deflated = new();
        int at = 8;
        while (at + 8 <= file.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(at, 4));
            string tag = Encoding.ASCII.GetString(file, at + 4, 4);
            int body = at + 8;
            if (tag == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(body, 4));
                height = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(body + 4, 4));
                colourType = file[body + 9];
                if (file[body + 8] != 8 || file[body + 12] != 0)
                {
                    return null;
                }
            }
            else if (tag == "PLTE")
            {
                palette = file[body..(body + length)];
            }
            else if (tag == "tRNS")
            {
                paletteAlpha = file[body..(body + length)];
            }
            else if (tag == "IDAT")
            {
                deflated.Write(file, body, length);
            }
            else if (tag == "IEND")
            {
                break;
            }
            at = body + length + 4;
        }
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        int samples = colourType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        if (samples == 0 || (colourType == 3 && palette.Length == 0))
        {
            return null;
        }
        return Pixels(Unfilter(Inflate(deflated.ToArray()), width, height, samples), width, height, colourType, palette, paletteAlpha);
    }

    private static byte[] Inflate(byte[] deflated)
    {
        MemoryStream raw = new();
        using (ZLibStream stream = new(new MemoryStream(deflated), CompressionMode.Decompress))
        {
            stream.CopyTo(raw);
        }
        return raw.ToArray();
    }

    /// <summary>
    /// Undo the per-row filters. Each row names a predictor over the pixel to its left, the one above and the one
    /// above-left, and the file stores only the difference from it — so a row cannot be read before the one above it.
    /// </summary>
    private static byte[] Unfilter(byte[] raw, int width, int height, int samples)
    {
        int stride = width * samples;
        byte[] pixels = new byte[stride * height];
        int at = 0;
        for (int row = 0; row < height; row++)
        {
            int filter = raw[at++];
            int line = row * stride;
            int above = line - stride;
            for (int index = 0; index < stride; index++)
            {
                int left = index >= samples ? pixels[line + index - samples] : 0;
                int up = row > 0 ? pixels[above + index] : 0;
                int upLeft = row > 0 && index >= samples ? pixels[above + index - samples] : 0;
                int predicted = filter switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => 0
                };
                pixels[line + index] = (byte)(raw[at + index] + predicted);
            }
            at += stride;
        }
        return pixels;
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        int estimate = left + up - upLeft;
        int toLeft = Math.Abs(estimate - left);
        int toUp = Math.Abs(estimate - up);
        int toUpLeft = Math.Abs(estimate - upLeft);
        if (toLeft <= toUp && toLeft <= toUpLeft)
        {
            return left;
        }
        return toUp <= toUpLeft ? up : upLeft;
    }

    private static PngImage Pixels(byte[] pixels, int width, int height, int colourType, byte[] palette, byte[] paletteAlpha)
    {
        int count = width * height;
        byte[] rgb = new byte[count * 3];
        byte[] alpha = new byte[count];
        bool transparent = false;
        for (int pixel = 0; pixel < count; pixel++)
        {
            byte opacity;
            if (colourType == 3)
            {
                int index = pixels[pixel];
                rgb[pixel * 3] = palette[index * 3];
                rgb[(pixel * 3) + 1] = palette[(index * 3) + 1];
                rgb[(pixel * 3) + 2] = palette[(index * 3) + 2];
                opacity = index < paletteAlpha.Length ? paletteAlpha[index] : byte.MaxValue;
            }
            else if (colourType is 0 or 4)
            {
                int step = colourType == 0 ? 1 : 2;
                byte grey = pixels[pixel * step];
                rgb[pixel * 3] = grey;
                rgb[(pixel * 3) + 1] = grey;
                rgb[(pixel * 3) + 2] = grey;
                opacity = colourType == 4 ? pixels[(pixel * 2) + 1] : byte.MaxValue;
            }
            else
            {
                int step = colourType == 2 ? 3 : 4;
                rgb[pixel * 3] = pixels[pixel * step];
                rgb[(pixel * 3) + 1] = pixels[(pixel * step) + 1];
                rgb[(pixel * 3) + 2] = pixels[(pixel * step) + 2];
                opacity = colourType == 6 ? pixels[(pixel * 4) + 3] : byte.MaxValue;
            }
            alpha[pixel] = opacity;
            transparent |= opacity != byte.MaxValue;
        }
        return new PngImage(width, height, rgb, transparent ? alpha : null);
    }
}
