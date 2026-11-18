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
    public async Task SameText_IsTheSameItem_WhateverOtherFormatsCameWithIt()
    {
        var plain = ClipCapture.Create(new ClipboardRawSnapshot([Unicode("Hi Bob")]), "", DateTime.Now)!;
        // OneNote/Word-style copy: extra formats that differ on every copy.
        var rich = ClipCapture.Create(new ClipboardRawSnapshot(
            [
                new ClipboardRawFormat(0xC001, "OneNote 2010 Internal Format", new byte[] { 1, 2, 3, 4 }),
                new ClipboardRawFormat(0xC002, "HTML Format", Encoding.UTF8.GetBytes("<b>Hi Bob</b> copied 09:31")),
                Unicode("Hi Bob"),
            ]), "", DateTime.Now)!;
        var richAgain = ClipCapture.Create(new ClipboardRawSnapshot(
            [
                new ClipboardRawFormat(0xC001, "OneNote 2010 Internal Format", new byte[] { 9, 9, 9 }),
                new ClipboardRawFormat(0xC002, "HTML Format", Encoding.UTF8.GetBytes("<b>Hi Bob</b> copied 14:02")),
                Unicode("Hi Bob"),
            ]), "", DateTime.Now)!;

        await Assert.That(rich.ContentHash).IsEqualTo(plain.ContentHash);
        await Assert.That(rich.HasSameContent(plain)).IsTrue();
        await Assert.That(richAgain.HasSameContent(rich)).IsTrue();
    }

    [Test]
    public async Task UnicodeTextPaddingAfterTheNull_DoesNotMakeANewItem()
    {
        // GlobalSize can report more than the text: bytes after the terminator are not content.
        byte[] padded = [.. Encoding.Unicode.GetBytes("x\0"), 0xAB, 0xCD, 0, 0];
        var a = ClipCapture.Create(new ClipboardRawSnapshot([Unicode("x")]), "", DateTime.Now)!;
        var b = ClipCapture.Create(new ClipboardRawSnapshot([new ClipboardRawFormat(13, "CF_UNICODETEXT", padded)]), "", DateTime.Now)!;

        await Assert.That(b.HasSameContent(a)).IsTrue();
    }

    [Test]
    public async Task DifferentText_OrTextVersusFiles_AreDifferentItems()
    {
        var bob = ClipCapture.Create(new ClipboardRawSnapshot([Unicode("Hi Bob")]), "", DateTime.Now)!;
        var bobs = ClipCapture.Create(new ClipboardRawSnapshot([Unicode("Hi Bob ")]), "", DateTime.Now)!;
        var pathText = ClipCapture.Create(new ClipboardRawSnapshot([Unicode("C:\\a.txt")]), "", DateTime.Now)!;

        byte[] paths = Encoding.Unicode.GetBytes("C:\\a.txt\0\0");
        byte[] drop = new byte[20 + paths.Length];
        BitConverter.GetBytes(20).CopyTo(drop, 0);
        BitConverter.GetBytes(1).CopyTo(drop, 16);
        paths.CopyTo(drop, 20);
        var file = ClipCapture.Create(new ClipboardRawSnapshot([new ClipboardRawFormat(15, "CF_HDROP", drop)]), "", DateTime.Now)!;

        await Assert.That(bobs.HasSameContent(bob)).IsFalse();
        await Assert.That(file.SearchText).IsEqualTo(pathText.SearchText);
        await Assert.That(file.HasSameContent(pathText)).IsFalse();
    }

    [Test]
    public async Task Images_AreTheSameItem_ByTheirImageBytesOnly()
    {
        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3];
        var a = ClipCapture.Create(new ClipboardRawSnapshot(
            [new ClipboardRawFormat(0xC003, "PNG", png), new ClipboardRawFormat(0xC004, "Chromium Web Custom MIME Data Format", new byte[] { 1 })]), "", DateTime.Now)!;
        var b = ClipCapture.Create(new ClipboardRawSnapshot(
            [new ClipboardRawFormat(0xC003, "PNG", png), new ClipboardRawFormat(0xC004, "Chromium Web Custom MIME Data Format", new byte[] { 2 })]), "", DateTime.Now)!;
        var other = ClipCapture.Create(new ClipboardRawSnapshot([new ClipboardRawFormat(0xC003, "PNG", new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 9 })]), "", DateTime.Now)!;

        await Assert.That(a.Kind).IsEqualTo(ClipKind.Image);
        await Assert.That(b.HasSameContent(a)).IsTrue();
        await Assert.That(other.HasSameContent(a)).IsFalse();
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
