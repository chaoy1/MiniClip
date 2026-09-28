namespace MiniClip.Diagnostics;

/// <summary>
/// A minimal, allocation-light counter for low-level keyboard hook callbacks.
/// </summary>
/// <remarks>
/// This exists because "the hook is installed" and "the hook is being called" are
/// different claims, and the second one is the only one that matters. <c>Install()</c>
/// returning a non-zero handle proves registration; nothing in the return value proves
/// Windows will actually invoke the callback. When Clipboard Mode appears to ignore the
/// arrow keys, the first thing worth knowing is which of those two failed.
/// <para>The recording path is deliberately as cheap as two integer increments, because
/// it runs inside a hook that must never be the reason Windows drops it for exceeding
/// <c>LowLevelHooksTimeout</c>. Nothing is written to disk from here.</para>
/// </remarks>
internal static class HookTrace
{
    private static int _callbacks;
    private static int _lastMessage;
    private static int _lastVirtualKey = -1;

    /// <summary>Number of times the hook callback has run since the process started.</summary>
    internal static int Callbacks => Volatile.Read(ref _callbacks);

    /// <summary>The last window message the hook saw (WM_KEYDOWN, WM_KEYUP, …).</summary>
    internal static int LastMessage => Volatile.Read(ref _lastMessage);

    /// <summary>The last virtual key code the hook saw, or -1.</summary>
    internal static int LastVirtualKey => Volatile.Read(ref _lastVirtualKey);

    internal static void Record(int nCode, IntPtr wParam)
    {
        Interlocked.Increment(ref _callbacks);
        Volatile.Write(ref _lastMessage, wParam.ToInt32());
    }

    internal static void RecordKey(uint virtualKey) => Volatile.Write(ref _lastVirtualKey, (int)virtualKey);

    internal static void Reset()
    {
        Volatile.Write(ref _callbacks, 0);
        Volatile.Write(ref _lastMessage, 0);
        Volatile.Write(ref _lastVirtualKey, -1);
    }

    internal static string Describe() =>
        $"callbacks={Callbacks} lastMsg=0x{LastMessage:X4} lastVk=0x{LastVirtualKey:X2}";
}
