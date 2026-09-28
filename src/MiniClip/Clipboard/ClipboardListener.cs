namespace MiniClip.Clipboard;

/// <summary>
/// Result of the read side of a clipboard update notification.
/// Handles the common Windows race where <c>WM_CLIPBOARDUPDATE</c> arrives
/// before the owning application has finished publishing its data.
/// </summary>
public static class ClipboardReader
{
    /// <summary>Attempts the read a few times with a short backoff. §17.</summary>
    public static async Task<string?> ReadTextAsync(int attempts = 4, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (Win32Clipboard.ContainsText())
            {
                var text = Win32Clipboard.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }

            if (attempt == attempts - 1)
            {
                break;
            }

            await Task.Delay(BackoffMilliseconds(attempt), ct).ConfigureAwait(false);
        }

        return null;
    }

    private static int BackoffMilliseconds(int attempt) => attempt switch
    {
        0 => 8,
        1 => 20,
        _ => 45,
    };

    /// <summary>True when the clipboard holds something but nothing readable as text (image, file list, HTML-only).</summary>
    public static bool HoldsNonTextContent() => !Win32Clipboard.ContainsText();
}

/// <summary>
/// Subscribes MiniClip's message window to Windows clipboard notifications and
/// raises a debounced, content-free event. Reading the text is the caller's job
/// so this type can stay ignorant of clipboard contents entirely.
/// </summary>
public sealed class ClipboardWatcher : IDisposable
{
    private readonly NativeMessageWindow _window;
    private bool _registered;
    private bool _disposed;

    public ClipboardWatcher(NativeMessageWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    /// <summary>
    /// Raised on the UI thread for each clipboard update, coalesced so a burst of
    /// notifications produces a single read. The sequence number is included so the
    /// caller can recognise — and ignore — writes MiniClip itself performed. §17.
    /// </summary>
    public event EventHandler<ClipboardUpdatedEventArgs>? Updated;

    public bool IsListening => _registered;

    /// <summary>Registers the clipboard format listener. Returns false if Windows refused.</summary>
    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_registered)
        {
            return true;
        }

        if (!_window.IsCreated && !_window.TryCreate())
        {
            return false;
        }

        _registered = Win32Clipboard.RegisterListener(_window.Handle);
        if (_registered)
        {
            _window.Message += OnWindowMessage;
        }

        return _registered;
    }

    public void Stop()
    {
        if (!_registered)
        {
            return;
        }

        _window.Message -= OnWindowMessage;
        Win32Clipboard.UnregisterListener(_window.Handle);
        _registered = false;
    }

    private void OnWindowMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != NativeConstants.WM_CLIPBOARDUPDATE)
        {
            return;
        }

        Updated?.Invoke(this, new ClipboardUpdatedEventArgs(Win32Clipboard.SequenceNumber));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}

public sealed class ClipboardUpdatedEventArgs : EventArgs
{
    public ClipboardUpdatedEventArgs(uint sequenceNumber) => SequenceNumber = sequenceNumber;

    /// <summary>Value of <c>GetClipboardSequenceNumber()</c> at the moment of the notification.</summary>
    public uint SequenceNumber { get; }
}
