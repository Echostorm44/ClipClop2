using System.Text;
using Cascade.UI;
using ClipClop2.History;
using ClipClop2.Storage;

namespace ClipClop2.Tests;

internal sealed class PinStoreTests
{
    [Test]
    public async Task Pins_RoundTripEncrypted_WithSameHash()
    {
        string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"clipclop2-pins-{Guid.NewGuid():N}.bin");
        try
        {
            var snapshot = new ClipboardRawSnapshot(
            [
                new ClipboardRawFormat(13, "CF_UNICODETEXT", Encoding.Unicode.GetBytes("pinned secret\0")),
                new ClipboardRawFormat(0xC0F0, "Rich Text Format", Encoding.ASCII.GetBytes(@"{\rtf1 pinned}")),
            ]);
            var entry = ClipCapture.Create(snapshot, @"C:\Apps\Word.exe", new DateTime(2026, 9, 30, 12, 0, 0))!;

            PinStore.Save(file, [entry]);
            var loaded = PinStore.Load(file).Single();

            await Assert.That(File.ReadAllBytes(file).AsSpan().IndexOf("pinned"u8)).IsEqualTo(-1);
            await Assert.That(loaded.ContentHash).IsEqualTo(entry.ContentHash);
            await Assert.That(loaded.HasSameContent(entry)).IsTrue();
            await Assert.That(loaded.Kind).IsEqualTo(entry.Kind);
            await Assert.That(loaded.SourceApp).IsEqualTo(@"C:\Apps\Word.exe");
            await Assert.That(loaded.CopiedAt).IsEqualTo(entry.CopiedAt);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task MissingFile_LoadsEmpty()
    {
        await Assert.That(PinStore.Load(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "does-not-exist.bin"))).IsEmpty();
    }
}
