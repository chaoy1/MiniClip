namespace MiniClip.History;

/// <summary>
/// Tracks which in-memory history revision has reached disk. An older successful write
/// must not acknowledge a newer change that was made while the write was in progress.
/// All calls occur on MiniClip's UI dispatcher.
/// </summary>
internal sealed class HistoryWriteTracker
{
    private long _version;
    private long _savedVersion;

    public long Version => _version;

    public bool IsDirty => _version != _savedVersion;

    public void MarkChanged() => _version++;

    public void MarkSaved(long version)
    {
        if (version > _savedVersion && version <= _version)
            _savedVersion = version;
    }
}
