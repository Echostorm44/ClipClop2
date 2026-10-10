namespace ClipClop2.History;

/// <summary>
/// The clipboard history: pinned entries first in the user's order, then unpinned entries newest
/// first. Copying something already present moves that entry instead of adding a duplicate.
/// Unpinned entries are evicted oldest-first to stay within the byte budget; pinned entries never are.
/// </summary>
/// <remarks>Owned by the UI thread; not thread-safe.</remarks>
internal sealed class HistoryStore
{
    private readonly List<ClipEntry> pinned = [];
    private readonly List<ClipEntry> unpinned = [];
    private long budgetBytes;
    private long unpinnedBytes;

    public HistoryStore(long budgetBytes)
    {
        this.budgetBytes = budgetBytes;
    }

    /// <summary>Raised after any change, so views can refresh their snapshot.</summary>
    public event Action? Changed;

    public int Count => pinned.Count + unpinned.Count;

    /// <summary>Bytes held by unpinned entries (what the budget limits).</summary>
    public long UnpinnedBytes => unpinnedBytes;

    /// <summary>Entry at a display position: pinned first, then unpinned newest-first.</summary>
    public ClipEntry this[int index] => index < pinned.Count ? pinned[index] : unpinned[index - pinned.Count];

    /// <summary>Display order as a fresh list (for the view; rebuilt only when <see cref="Changed"/> fires).</summary>
    public List<ClipEntry> Snapshot()
    {
        var all = new List<ClipEntry>(Count);
        all.AddRange(pinned);
        all.AddRange(unpinned);
        return all;
    }

    /// <summary>
    /// Records a copy. Returns the entry that now represents it: the existing one when the content was
    /// already in history (moved to the top of the unpinned section, or left in place if pinned).
    /// </summary>
    public ClipEntry Add(ClipEntry entry)
    {
        ClipEntry? existing = FindSameContent(entry);
        if (existing is not null)
        {
            entry.Release();
            existing.CopiedAt = entry.CopiedAt;
            if (!existing.Pinned)
            {
                unpinned.Remove(existing);
                unpinned.Insert(0, existing);
            }
            Changed?.Invoke();
            return existing;
        }

        unpinned.Insert(0, entry);
        unpinnedBytes += entry.SizeBytes;
        EvictToBudget();
        Changed?.Invoke();
        return entry;
    }

    /// <summary>
    /// The entry was just put back on the clipboard (pasted or copied): it becomes the newest unpinned
    /// entry, as if copied again. Pinned entries keep their place.
    /// </summary>
    public void Promote(ClipEntry entry)
    {
        entry.CopiedAt = DateTime.Now;
        if (!entry.Pinned && unpinned.Remove(entry))
        {
            unpinned.Insert(0, entry);
        }
        Changed?.Invoke();
    }

    public void Pin(ClipEntry entry)
    {
        if (entry.Pinned || !unpinned.Remove(entry))
        {
            return;
        }
        unpinnedBytes -= entry.SizeBytes;
        entry.Pinned = true;
        pinned.Add(entry);
        Changed?.Invoke();
    }

    public void Unpin(ClipEntry entry)
    {
        if (!entry.Pinned || !pinned.Remove(entry))
        {
            return;
        }
        entry.Pinned = false;
        InsertUnpinnedByTime(entry);
        unpinnedBytes += entry.SizeBytes;
        EvictToBudget();
        Changed?.Invoke();
    }

    public void Remove(ClipEntry entry)
    {
        if (entry.Pinned ? pinned.Remove(entry) : RemoveUnpinned(entry))
        {
            entry.Release();
            Changed?.Invoke();
        }
    }

    /// <summary>Drops every unpinned entry (and its cache files); pins stay.</summary>
    public void ClearUnpinned()
    {
        if (unpinned.Count == 0)
        {
            return;
        }
        foreach (ClipEntry entry in unpinned)
        {
            entry.Release();
        }
        unpinned.Clear();
        unpinnedBytes = 0;
        Changed?.Invoke();
    }

    /// <summary>Adds entries restored from disk at startup (all pinned, in saved order).</summary>
    public void RestorePinned(IEnumerable<ClipEntry> entries)
    {
        foreach (ClipEntry entry in entries)
        {
            entry.Pinned = true;
            pinned.Add(entry);
        }
        Changed?.Invoke();
    }

    public IReadOnlyList<ClipEntry> PinnedEntries => pinned;

    public void SetBudget(long bytes)
    {
        budgetBytes = bytes;
        if (EvictToBudget())
        {
            Changed?.Invoke();
        }
    }

    private ClipEntry? FindSameContent(ClipEntry candidate)
    {
        foreach (ClipEntry entry in pinned)
        {
            if (entry.HasSameContent(candidate))
            {
                return entry;
            }
        }
        foreach (ClipEntry entry in unpinned)
        {
            if (entry.HasSameContent(candidate))
            {
                return entry;
            }
        }
        return null;
    }

    // Unpinning keeps the entry where its copy time puts it among unpinned entries.
    private void InsertUnpinnedByTime(ClipEntry entry)
    {
        int index = 0;
        while (index < unpinned.Count && unpinned[index].CopiedAt > entry.CopiedAt)
        {
            index++;
        }
        unpinned.Insert(index, entry);
    }

    private bool RemoveUnpinned(ClipEntry entry)
    {
        if (!unpinned.Remove(entry))
        {
            return false;
        }
        unpinnedBytes -= entry.SizeBytes;
        return true;
    }

    // The newest entry always stays, even if it alone exceeds the budget: the user just copied it.
    private bool EvictToBudget()
    {
        bool evicted = false;
        while (unpinnedBytes > budgetBytes && unpinned.Count > 1)
        {
            ClipEntry oldest = unpinned[^1];
            unpinned.RemoveAt(unpinned.Count - 1);
            unpinnedBytes -= oldest.SizeBytes;
            oldest.Release();
            evicted = true;
        }
        return evicted;
    }
}
