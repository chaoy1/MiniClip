namespace MiniClip.Paste;

using System.Runtime.InteropServices;

/// <summary>
/// A resolved destination for a paste: the window that was in the foreground when the
/// popup opened, plus the focused control inside it. Captured before the popup is shown
/// so the guard can tell "the user is still where they were" from "the user moved on". §18.
/// </summary>
public sealed class PasteTarget
{
    private PasteTarget(Win32Focus.FocusSnapshot snapshot, IntPtr window, string description)
    {
        Snapshot = snapshot;
        Window = window;
        Description = description;
        CapturedAt = DateTimeOffset.Now;
    }

    public Win32Focus.FocusSnapshot Snapshot { get; }

    /// <summary>The top-level window that should receive the paste.</summary>
    public IntPtr Window { get; }

    /// <summary>Human-readable destination name for the popup's status line.</summary>
    public string Description { get; }

    public DateTimeOffset CapturedAt { get; }

    public static PasteTarget Capture()
    {
        var snapshot = Win32Focus.CaptureForegroundFocus();
        var window = Win32Focus.RootOf(snapshot.ActiveWindow != IntPtr.Zero ? snapshot.ActiveWindow : snapshot.FocusedWindow);
        return new PasteTarget(snapshot, window, Win32Focus.DescribeTarget(window));
    }

    public static PasteTarget Empty { get; } = new(Win32Focus.FocusSnapshot.Empty, IntPtr.Zero, "未知窗口");

    /// <summary>True when the user is still focused on the window we captured.</summary>
    public bool StillValid()
    {
        if (Window == IntPtr.Zero)
        {
            return false;
        }

        if (Win32Focus.RootOf(Win32Windows.GetForegroundWindow()) != Window)
        {
            return false;
        }

        var current = Win32Focus.CaptureForWindow(Window);
        if (!current.IsUsable)
        {
            // We cannot observe the thread any more; the foreground check above is
            // the strongest signal available, so trust it rather than refusing to paste.
            return true;
        }

        return Snapshot.MatchesTarget(current);
    }
}

public enum PasteStatus
{
    /// <summary>Text is in the clipboard and Ctrl+V was delivered. The system clipboard keeps the text. §1.3.</summary>
    Success,

    /// <summary>The user moved to another window between opening the popup and confirming.</summary>
    TargetChanged,

    /// <summary>No usable destination was recorded, or it disappeared.</summary>
    NoTarget,

    /// <summary>Windows refused the clipboard write. Nothing was sent anywhere.</summary>
    ClipboardFailed,

    /// <summary>Another copy replaced MiniClip's text before Ctrl+V could be sent.</summary>
    ClipboardChanged,

    /// <summary>Ctrl+V could not be delivered. The text is still in the clipboard for a manual paste. §1.3.</summary>
    InputFailed,

    /// <summary>The destination is elevated and MiniClip is not, so SendInput cannot reach it. Known V1 limitation. §28.2.</summary>
    ElevatedTarget,
}

/// <summary>Outcome of an <see cref="PasteController.PasteAsync"/> call, with copy ready for the status line.</summary>
public sealed record PasteResult(PasteStatus Status, string? Text = null)
{
    public bool Succeeded => Status == PasteStatus.Success;

    /// <summary>True when the text already reached the system clipboard, even if the keystroke did not land.</summary>
    public bool ClipboardHoldsText => Status is PasteStatus.Success or PasteStatus.InputFailed or PasteStatus.ElevatedTarget;

    public string Message => Status switch
    {
        PasteStatus.Success => "已粘贴",
        PasteStatus.TargetChanged => "窗口已切换，未粘贴",
        PasteStatus.NoTarget => "找不到原来的输入窗口",
        PasteStatus.ClipboardFailed => "剪贴板被占用，已取消粘贴",
        PasteStatus.ClipboardChanged => "剪贴板已变化，已取消自动粘贴",
        PasteStatus.InputFailed => "无法自动粘贴，文本已在剪贴板",
        PasteStatus.ElevatedTarget => "目标窗口以管理员身份运行，请手动粘贴",
        _ => "未粘贴",
    };
}

/// <summary>
/// Owns the confirm half of Clipboard Mode: verify the destination is unchanged,
/// put the text on the clipboard, then inject Ctrl+V. §18, §29 stages 4–6.
/// </summary>
public sealed class PasteController
{
    /// <summary>
    /// Time given to the destination window to finish handling the clipboard update
    /// before the keystroke arrives. Too short and applications paste the *previous*
    /// clipboard content, which is the classic clipboard-manager bug.
    /// </summary>
    public const int SettleMilliseconds = 55;

    private uint _lastSelfWriteSequence;

    /// <summary>
    /// Sequence number of the most recent clipboard write MiniClip performed, so the
    /// clipboard watcher can recognise its own echo and not re-record the clip. §17.
    /// </summary>
    public uint LastSelfWriteSequence => _lastSelfWriteSequence;

    /// <summary>
    /// Writes <paramref name="text"/> to the clipboard and delivers Ctrl+V to
    /// <paramref name="target"/>. Never sends input to a window other than the one
    /// captured when the popup opened.
    /// </summary>
    public async Task<PasteResult> PasteAsync(string text, PasteTarget target, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new PasteResult(PasteStatus.NoTarget);
        }

        ArgumentNullException.ThrowIfNull(target);

        if (target.Window == IntPtr.Zero || !Win32Windows.IsWindow(target.Window))
        {
            return new PasteResult(PasteStatus.NoTarget);
        }

        // Re-check immediately before acting: the popup may have been open for a while. §18, §25 scenario 6.
        if (!target.StillValid())
        {
            return new PasteResult(PasteStatus.TargetChanged);
        }

        // An elevated destination silently ignores SendInput from a normal-integrity
        // process. Detect it up front so we can explain instead of falsely reporting success. §28.2.
        if (!Win32WindowStyles.CanSendInputTo(target.Window))
        {
            if (!WriteClipboard(text, out var elevatedWriteError))
            {
                return new PasteResult(PasteStatus.ClipboardFailed, elevatedWriteError);
            }

            return new PasteResult(PasteStatus.ElevatedTarget);
        }

        if (!WriteClipboard(text, out var writeError))
        {
            return new PasteResult(PasteStatus.ClipboardFailed, writeError);
        }

        var delivery = await DeliverAfterWriteAsync(target.StillValid,
            () => Win32Clipboard.SequenceNumber, _lastSelfWriteSequence, Win32Input.SendCtrlV, ct)
            .ConfigureAwait(false);
        return new PasteResult(delivery);
    }

    internal static async Task<PasteStatus> DeliverAfterWriteAsync(Func<bool> targetStillValid,
        Func<uint> sequenceNumber, uint writtenSequence, Func<bool> sendCtrlV,
        CancellationToken ct = default)
    {
        // Let the destination finish processing the clipboard update, then confirm both
        // the destination and the payload still belong to this paste operation.
        await Task.Delay(SettleMilliseconds, ct).ConfigureAwait(false);
        if (!targetStillValid())
            return PasteStatus.TargetChanged;

        var currentSequence = sequenceNumber();
        if (writtenSequence != 0 && currentSequence != 0 && currentSequence != writtenSequence)
            return PasteStatus.ClipboardChanged;

        return sendCtrlV() ? PasteStatus.Success : PasteStatus.InputFailed;
    }

    private bool WriteClipboard(string text, out string? error)
    {
        _lastSelfWriteSequence = Win32Clipboard.SequenceNumber;

        if (!Win32Clipboard.SetText(text))
        {
            error = NativeErrors.Describe(Marshal.GetLastWin32Error());
            return false;
        }

        _lastSelfWriteSequence = Win32Clipboard.SequenceNumber;
        error = null;
        return true;
    }

    /// <summary>
    /// Releases modifier keys the user is still physically holding after the popup took
    /// over the keystroke. Without this the destination application still believes Alt
    /// is down and interprets the next real keypress as an Alt shortcut. §9.2.
    /// </summary>
    public static void ReleaseHeldModifiers()
    {
        uint[] modifiers = [NativeConstants.VK_MENU, NativeConstants.VK_CONTROL, NativeConstants.VK_SHIFT];
        foreach (var key in modifiers)
        {
            if (Win32Input.IsKeyDown((int)key))
            {
                Win32Input.SendKeyEvent(key, keyUp: true);
            }
        }
    }
}
