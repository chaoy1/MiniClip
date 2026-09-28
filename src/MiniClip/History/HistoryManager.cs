namespace MiniClip.History;

/// <summary>
/// Owns the in-memory clipboard history: newest first, newest wins, exact-string
/// deduplication, hard cap. Every mutation is applied on the UI dispatcher so the
/// observable list bound to the popup is only ever touched from one thread.
/// </summary>
public sealed class HistoryManager
{
    /// <summary>Keep at most the 100 most recent distinct entries.</summary>
    public const int DefaultCapacity = 100;

    /// <summary>Bounds the text retained in memory even when all 100 entries are large.</summary>
    public const int MaxTotalCharacters = 500_000;

    /// <summary>Refuse anything larger than this; a giant clip would stall the popup and the disk write. §10.1.</summary>
    public const int MaxEntryLength = 100_000;

    private readonly List<HistoryEntry> _entries = new(DefaultCapacity + 1);
    private readonly Dispatcher _dispatcher;
    private int _totalCharacters;

    public HistoryManager(Dispatcher dispatcher, int capacity = DefaultCapacity)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        Capacity = Math.Clamp(capacity, 1, 500);
    }

    public int Capacity { get; }

    /// <summary>Raised on the UI thread after any mutation, including a clear.</summary>
    public event EventHandler? Changed;

    public int Count => _entries.Count;

    public bool IsEmpty => _entries.Count == 0;

    /// <summary>Snapshot of the current list, newest first.</summary>
    public IReadOnlyList<HistoryEntry> Snapshot() => _entries.ToArray();

    public HistoryEntry? At(int index) =>
        index >= 0 && index < _entries.Count ? _entries[index] : null;

    /// <summary>
    /// Records a new clip. Returns what happened so the caller can decide whether a
    /// disk write and a UI refresh are warranted.
    /// </summary>
    /// <remarks>
    /// §11: an exact repeat of the current top entry is ignored entirely; an exact
    /// repeat of an older entry is promoted to the top instead of duplicated.
    /// </remarks>
    public async Task<HistoryChange> AddAsync(string? text, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return HistoryChange.Ignored(HistoryChangeReason.EmptyText);
        }

        if (text.Length > MaxEntryLength)
        {
            // Deliberately does not include the text itself — the diagnostic must stay content-free.
            return HistoryChange.Ignored(HistoryChangeReason.TooLong);
        }

        return await OnUiThreadAsync(() =>
        {
            if (_entries.Count > 0 && string.Equals(_entries[0].Text, text, StringComparison.Ordinal))
            {
                return HistoryChange.Ignored(HistoryChangeReason.DuplicateOfTop);
            }

            var now = DateTimeOffset.Now;
            var existing = IndexOf(text);
            if (existing >= 0)
            {
                _entries.RemoveAt(existing);
                _entries.Insert(0, new HistoryEntry(text, now));
                RaiseChanged();
                return HistoryChange.Promoted(existing);
            }

            _entries.Insert(0, new HistoryEntry(text, now));
            _totalCharacters += text.Length;
            var evicted = false;
            while (_entries.Count > Capacity || _totalCharacters > MaxTotalCharacters)
            {
                _totalCharacters -= _entries[^1].Text.Length;
                _entries.RemoveAt(_entries.Count - 1);
                evicted = true;
            }

            RaiseChanged();
            return HistoryChange.Added(evicted);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces the whole list — used when loading from disk. Entries keep the order
    /// they were given, duplicates are collapsed (first occurrence wins) and the cap
    /// is applied to the tail.
    /// </summary>
    public Task<int> ResetAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        return OnUiThreadAsync(() =>
        {
            _entries.Clear();
            _totalCharacters = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var now = DateTimeOffset.Now;

            foreach (var text in texts)
            {
                if (string.IsNullOrEmpty(text) || text.Length > MaxEntryLength)
                {
                    continue;
                }

                if (!seen.Add(text))
                {
                    continue;
                }

                if (_totalCharacters + text.Length > MaxTotalCharacters)
                {
                    break;
                }

                _entries.Add(new HistoryEntry(text, now));
                _totalCharacters += text.Length;
                if (_entries.Count >= Capacity)
                {
                    break;
                }
            }

            RaiseChanged();
            return _entries.Count;
        }, ct);
    }

    /// <summary>
    /// Empties the history. The caller is responsible for deleting the on-disk copy —
    /// §27 requires the tray action to clear memory *and* disk.
    /// </summary>
    public Task<bool> ClearAsync(CancellationToken ct = default)
    {
        return OnUiThreadAsync(() =>
        {
            if (_entries.Count == 0)
            {
                return false;
            }

            _entries.Clear();
            _totalCharacters = 0;
            RaiseChanged();
            return true;
        }, ct);
    }

    /// <summary>Removes a single entry by index (used by the popup's delete affordance, if any).</summary>
    public Task<bool> RemoveAtAsync(int index, CancellationToken ct = default)
    {
        return OnUiThreadAsync(() =>
        {
            if (index < 0 || index >= _entries.Count)
            {
                return false;
            }

            _totalCharacters -= _entries[index].Text.Length;
            _entries.RemoveAt(index);
            RaiseChanged();
            return true;
        }, ct);
    }

    /// <summary>Stable exact-match lookup. Comparison is ordinal, never case- or whitespace-insensitive. §12.</summary>
    private int IndexOf(string text)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (string.Equals(_entries[i].Text, text, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private Task<T> OnUiThreadAsync<T>(Func<T> work, CancellationToken ct)
    {
        if (_dispatcher.CheckAccess())
        {
            return Task.FromResult(work());
        }

        return _dispatcher.InvokeAsync(work, DispatcherPriority.Normal, ct).Task;
    }
}

public enum HistoryChangeReason
{
    NewEntry,
    PromotedExisting,
    DuplicateOfTop,
    EmptyText,
    TooLong,
}

/// <summary>Outcome of an <see cref="HistoryManager.AddAsync"/> call.</summary>
public readonly record struct HistoryChange(HistoryChangeReason Reason, int PreviousIndex, bool Evicted)
{
    /// <summary>True when the visible list changed and the caller should persist + refresh.</summary>
    public bool ListChanged => Reason is HistoryChangeReason.NewEntry or HistoryChangeReason.PromotedExisting;

    public static HistoryChange Added(bool evicted) => new(HistoryChangeReason.NewEntry, -1, evicted);

    public static HistoryChange Promoted(int previousIndex) => new(HistoryChangeReason.PromotedExisting, previousIndex, false);

    public static HistoryChange Ignored(HistoryChangeReason reason) => new(reason, -1, false);

    public static HistoryChange None { get; } = Ignored(HistoryChangeReason.DuplicateOfTop);
}
