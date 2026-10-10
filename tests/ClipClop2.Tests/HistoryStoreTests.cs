using System.IO.Hashing;
using System.Text;
using ClipClop2.History;

namespace ClipClop2.Tests;

internal sealed class HistoryStoreTests
{
    private static DateTime clock = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Local);

    private static ClipEntry Text(string text, int padBytes = 0)
    {
        byte[] data = Encoding.Unicode.GetBytes(text + new string(' ', padBytes / 2));
        clock = clock.AddSeconds(1);
        return new ClipEntry(ClipKind.Text, [new ClipFormat(13, "CF_UNICODETEXT", data)], XxHash128.HashToUInt128(data), text, "Test", clock);
    }

    [Test]
    public async Task NewestCopyIsFirst()
    {
        var store = new HistoryStore(1_000_000);
        store.Add(Text("one"));
        store.Add(Text("two"));

        await Assert.That(store[0].SearchText).IsEqualTo("two");
        await Assert.That(store[1].SearchText).IsEqualTo("one");
    }

    [Test]
    public async Task RecopyMovesExistingEntryInsteadOfDuplicating()
    {
        var store = new HistoryStore(1_000_000);
        ClipEntry first = store.Add(Text("repeat"));
        store.Add(Text("other"));

        ClipEntry again = store.Add(Text("repeat"));

        await Assert.That(store.Count).IsEqualTo(2);
        await Assert.That(ReferenceEquals(again, first)).IsTrue();
        await Assert.That(store[0].SearchText).IsEqualTo("repeat");
    }

    [Test]
    public async Task PinnedEntriesStayOnTopInPinOrder()
    {
        var store = new HistoryStore(1_000_000);
        ClipEntry a = store.Add(Text("a"));
        ClipEntry b = store.Add(Text("b"));
        store.Pin(a);
        store.Pin(b);

        store.Add(Text("newest"));

        await Assert.That(store[0]).IsSameReferenceAs(a);
        await Assert.That(store[1]).IsSameReferenceAs(b);
        await Assert.That(store[2].SearchText).IsEqualTo("newest");
    }

    [Test]
    public async Task RecopyOfPinnedContentLeavesItPinnedInPlace()
    {
        var store = new HistoryStore(1_000_000);
        ClipEntry pinnedEntry = store.Add(Text("keep"));
        store.Pin(pinnedEntry);
        store.Add(Text("x"));

        store.Add(Text("keep"));

        await Assert.That(store.Count).IsEqualTo(2);
        await Assert.That(store[0]).IsSameReferenceAs(pinnedEntry);
        await Assert.That(pinnedEntry.Pinned).IsTrue();
    }

    [Test]
    public async Task OldestUnpinnedEntriesFallOffPastTheBudget()
    {
        // Same length as the "itemN" entries below, so it has exactly their size.
        ClipEntry sample = Text("itemX", padBytes: 1000);
        var store = new HistoryStore(budgetBytes: sample.SizeBytes * 3);

        for (int i = 0; i < 5; i++)
        {
            store.Add(Text($"item{i}", padBytes: 1000));
        }

        await Assert.That(store.Count).IsEqualTo(3);
        await Assert.That(store[2].SearchText).IsEqualTo("item2");
        await Assert.That(store.UnpinnedBytes <= sample.SizeBytes * 3).IsTrue();
    }

    [Test]
    public async Task PinnedEntriesAreNeverEvictedOrCounted()
    {
        // Same length as the "itemN" entries below, so it has exactly their size.
        ClipEntry sample = Text("itemX", padBytes: 1000);
        var store = new HistoryStore(budgetBytes: sample.SizeBytes * 2);
        ClipEntry keep = store.Add(Text("keep", padBytes: 1000));
        store.Pin(keep);

        for (int i = 0; i < 4; i++)
        {
            store.Add(Text($"item{i}", padBytes: 1000));
        }

        await Assert.That(store[0]).IsSameReferenceAs(keep);
        await Assert.That(store.Count).IsEqualTo(3);
    }

    [Test]
    public async Task LoweringTheBudgetEvictsImmediately()
    {
        var store = new HistoryStore(10_000_000);
        for (int i = 0; i < 10; i++)
        {
            store.Add(Text($"item{i}", padBytes: 1000));
        }

        store.SetBudget(store[0].SizeBytes * 4);

        await Assert.That(store.Count).IsEqualTo(4);
        await Assert.That(store[0].SearchText).IsEqualTo("item9");
    }

    [Test]
    public async Task UnpinReturnsEntryToItsTimePosition()
    {
        var store = new HistoryStore(1_000_000);
        ClipEntry old = store.Add(Text("old"));
        store.Pin(old);
        store.Add(Text("newer"));
        store.Add(Text("newest"));

        store.Unpin(old);

        await Assert.That(store[0].SearchText).IsEqualTo("newest");
        await Assert.That(store[2]).IsSameReferenceAs(old);
    }

    [Test]
    public async Task ClearUnpinned_KeepsPins_AndFreesTheBudget()
    {
        var store = new HistoryStore(1_000_000);
        ClipEntry pinned = store.Add(Text("keep"));
        store.Pin(pinned);
        store.Add(Text("drop one"));
        store.Add(Text("drop two"));

        store.ClearUnpinned();

        await Assert.That(store.Count).IsEqualTo(1);
        await Assert.That(store[0]).IsSameReferenceAs(pinned);
        await Assert.That(store.UnpinnedBytes).IsEqualTo(0L);
    }
}
