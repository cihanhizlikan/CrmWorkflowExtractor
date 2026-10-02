using System.Buffers.Binary;

namespace Crm.Cli.Reports;

/// <summary>
/// A PNG's size, read from its header.
///
/// <para>
/// It used to decode the pixels as well, because a PDF carries a picture as raw samples and had to be handed
/// them. A .docx carries the file itself, so the only thing anyone needs to know about the logo is how big it is
/// — Word is told the box to draw it in, and the aspect ratio has to come from somewhere or the mark is
/// stretched. The inflating and unfiltering went with the PDF.
/// </para>
/// </summary>
public sealed class PngImage(int width, int height)
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public int Width { get; } = width;

    public int Height { get; } = height;

    /// <summary>
    /// The size, or null when the bytes are not a PNG at all. Null rather than an exception: a logo is a mark on a
    /// cover, and a run does not fail over one.
    /// </summary>
    public static PngImage? TryRead(byte[] file)
    {
        // Signature, then the IHDR chunk's length and name, then width and height: 24 bytes before anything else.
        if (file.Length < 24 || !file.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            return null;
        }
        if (!file.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }
        int width = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(20, 4));
        return width > 0 && height > 0 ? new PngImage(width, height) : null;
    }
}
