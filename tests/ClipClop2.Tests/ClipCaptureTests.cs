using System.Text;
using Cascade.UI;
using ClipClop2.History;
using ClipClop2.Storage;

namespace ClipClop2.Tests;

internal sealed class ClipCaptureTests
{
    private static ClipboardRawFormat Unicode(string text) => new(13, "CF_UNICODETEXT", Encoding.Unicode.GetBytes(text + "\0"));

    [Test]
    [Arguments("hello world", ClipKind.Text)]
    [Arguments("https://example.com/a?b=1", ClipKind.Link)]
    [Arguments("someone@example.com", ClipKind.Email)]
    [Arguments("#1E90FF", ClipKind.Color)]
    [Arguments("rgb(30, 144, 255)", ClipKind.Color)]
    [Arguments("not a link https://x.com", ClipKind.Text)]
    public async Task Text_IsClassified(string text, ClipKind expected)
    {
        var entry = ClipCapture.Create(new ClipboardRawSnapshot([Unicode(text)]), "", DateTime.Now)!;

        await Assert.That(entry.Kind).IsEqualTo(expected);
        await Assert.That(entry.SearchText).IsEqualTo(text);
    }

    [Test]
    public async Task Files_KeepPathsOnly()
    {
        // DROPFILES: pFiles = 20, fWide = 1, then two UTF-16 paths and a final null.
        byte[] paths = Encoding.Unicode.GetBytes("C:\\a.mp4\0C:\\b.txt\0\0");
        byte[] drop = new byte[20 + paths.Length];
        BitConverter.GetBytes(20).CopyTo(drop, 0);
        BitConverter.GetBytes(1).CopyTo(drop, 16);
        paths.CopyTo(drop, 20);

        var entry = ClipCapture.Create(new ClipboardRawSnapshot([new ClipboardRawFormat(15, "CF_HDROP", drop)]), "", DateTime.Now)!;

        await Assert.That(entry.Kind).IsEqualTo(ClipKind.Files);
        await Assert.That(entry.SearchText).IsEqualTo("C:\\a.mp4\nC:\\b.txt");
    }

    [Test]
    public async Task SameContent_SameHash_DifferentFormatName_DifferentHash()
    {
        var a = ClipCapture.Create(new ClipboardRawSnapshot([Unicode("x")]), "", DateTime.Now)!;
        var b = ClipCapture.Create(new ClipboardRawSnapshot([Unicode("x")]), "", DateTime.Now)!;
        var c = ClipCapture.Create(new ClipboardRawSnapshot([new ClipboardRawFormat(0xC001, "Custom", Encoding.Unicode.GetBytes("x\0")), Unicode("x")]), "", DateTime.Now)!;

        await Assert.That(a.ContentHash).IsEqualTo(b.ContentHash);
        await Assert.That(a.HasSameContent(b)).IsTrue();
        await Assert.That(c.ContentHash).IsNotEqualTo(a.ContentHash);
    }

    [Test]
    public async Task ProcessPointerFormats_AreNotCaptured()
    {
        await Assert.That(ClipCapture.IncludeFormat("Ole Private Data")).IsFalse();
        await Assert.That(ClipCapture.IncludeFormat("FileContents")).IsFalse();
        await Assert.That(ClipCapture.IncludeFormat("Rich Text Format")).IsTrue();
    }

    [Test]
    public async Task LargePayload_SpillsEncrypted_AndReadsBack()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clipclop2-test-" + Guid.NewGuid().ToString("N"));
        var cache = new PayloadCache(dir);
        try
        {
            byte[] big = new byte[PayloadCache.SpillThresholdBytes + 10];
            Random.Shared.NextBytes(big);
            var format = new ClipFormat(0xC002, "PNG", big);

            format.Spill(cache);
            string file = Directory.GetFiles(dir).Single();

            await Assert.That(format.IsCached).IsTrue();
            await Assert.That(format.Length).IsEqualTo(big.Length);
            await Assert.That(File.ReadAllBytes(file).AsSpan().IndexOf(big.AsSpan(0, 64))).IsEqualTo(-1); // encrypted on disk
            await Assert.That(format.ReadData()).IsEquivalentTo(big);

            format.DeleteCache();
            await Assert.That(Directory.GetFiles(dir)).IsEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
