using System.Buffers.Binary;
using System.Text;
using Cascade.UI;
using ClipClop2.History;
using ClipClop2.Ui;

namespace ClipClop2.Tests;

internal sealed class ClipImagesTests
{
    // A packed 2×1 32-bpp BI_BITFIELDS DIB (v3 header + three masks), RGBX byte order: red, blue.
    private static byte[] BitfieldsDib()
    {
        byte[] dib = new byte[40 + 12 + 8];
        BinaryPrimitives.WriteInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), -1);
        BinaryPrimitives.WriteInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteInt16LittleEndian(dib.AsSpan(14), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(40), 0x000000FF);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(44), 0x0000FF00);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(48), 0x00FF0000);
        byte[] pixels = [0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x00];
        pixels.CopyTo(dib, 52);
        return dib;
    }

    private static ClipEntry Image(params ClipFormat[] formats)
    {
        return new ClipEntry(ClipKind.Image, formats, 1, "", "", DateTime.Now);
    }

    [Test]
    public async Task Size_ComesFromTheDibHeader()
    {
        var entry = Image(new ClipFormat(8, "CF_DIB", BitfieldsDib()));

        await Assert.That(entry.ImageSize).IsEqualTo((2, 1));
        await Assert.That(entry.DisplayLine).IsEqualTo("Image (2×1)");
    }

    [Test]
    public async Task Thumbnail_DecodesABitfieldsDib_WithItsMasks_AndOpaque()
    {
        var entry = Image(new ClipFormat(8, "CF_DIB", BitfieldsDib()));

        using ImageSource? image = ClipImages.DecodeThumbnail(entry, 100, 100);

        await Assert.That(image).IsNotNull();
        await Assert.That(image!.Width).IsEqualTo(2);
        await Assert.That(image.Height).IsEqualTo(1);
    }

    [Test]
    public async Task Emf_HasNoThumbnail()
    {
        var entry = Image(new ClipFormat(14, "CF_ENHMETAFILE", [1, 2, 3]));

        await Assert.That(ClipImages.DecodeThumbnail(entry, 100, 100)).IsNull();
    }

    [Test]
    [Arguments("first line\nsecond", "first line")]
    [Arguments("\n\n   \n  indented  \nnext", "indented")]
    [Arguments("", "")]
    public async Task FirstLine_IsTheFirstNonBlankLine(string text, string expected)
    {
        await Assert.That(ClipImages.FirstLine(text)).IsEqualTo(expected);
    }

    [Test]
    public async Task FirstLine_IsCapped()
    {
        string line = ClipImages.FirstLine(new string('x', 1000));

        await Assert.That(line.Length).IsEqualTo(241);
        await Assert.That(line).EndsWith("…");
    }

    [Test]
    [Arguments("#1E90FF", true)]
    [Arguments("#1E9", true)]
    [Arguments("rgb(30, 144, 255)", true)]
    [Arguments("rgba(30,144,255,0.5)", true)]
    [Arguments("#GGGGGG", false)]
    [Arguments("hsl(10, 20%, 30%)", false)]
    public async Task ParseColor_AcceptsHexAndRgb(string text, bool parses)
    {
        await Assert.That(Look.ParseColor(text) is not null).IsEqualTo(parses);
    }

    [Test]
    public async Task Files_DisplayFirstNameAndCount()
    {
        var entry = new ClipEntry(ClipKind.Files, [new ClipFormat(15, "CF_HDROP", [0])], 1, "C:\\a\\one.txt\nC:\\b\\two.txt\nC:\\c\\three.txt", "", DateTime.Now);

        await Assert.That(entry.DisplayLine).IsEqualTo("one.txt + 2 more");
    }
}
