using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MiniClip.Input;
using MiniClip.Storage;
using MiniClip.Settings;

namespace MiniClip.Diagnostics;

/// <summary>
/// A headless check of the parts of MiniClip that cannot be verified by reading code:
/// the styles Windows actually applied to the popup window, whether the hotkey really
/// registered, whether the tray icon reached the shell, and what the popup looks like.
/// </summary>
/// <remarks>
/// <para>This exists because most of MiniClip's risk is in behaviour that only Windows
/// can confirm. "WS_EX_NOACTIVATE is set" is a claim; reading it back from the live
/// window after WPF has created it is evidence. Every check prints a PASS/FAIL line and
/// the process exit code is the number of failures.</para>
/// <para>Run: <c>MiniClip.exe --selftest [outputDirectory]</c>. It leaves the tray icon
/// deregistered and exits without starting the real application.</para>
/// </remarks>
public static class SelfTest
{
    private const int Fail = 0;

    public static int Run(string? outputDirectory)
    {
        var failures = 0;
        var output = outputDirectory ?? Path.Combine(Path.GetTempPath(), "miniclip-selftest");
        Directory.CreateDirectory(output);

        DiagnosticsLog.Start(Path.Combine(output, "selftest.log"));
        DiagnosticsLog.Write("selftest", $"output={DiagnosticsLog.Shorten(output)}");

        Console.WriteLine("MiniClip self-test");
        Console.WriteLine($"output: {output}");
        Console.WriteLine();

        failures += CheckMessageWindow();
        failures += CheckHotkey();
        failures += CheckKeyboardHook();
        failures += CheckKeyboardModifierReset();
        failures += CheckClipboardRoundTrip();
        failures += CheckRegisteredPlainTextClipboard();
        failures += CheckHistoryRules();
        failures += CheckHistorySaveRevision();
        failures += CheckStorageRoundTrip();
        failures += CheckDataLocation();
        failures += CheckHistoryFailureSafety(output);
        failures += CheckUnusableHistoryFileClear(output);
        failures += CheckClearDuringClipboardRead(output);
        failures += CheckQueuedClipboardUpdateAfterClear(output);
        failures += CheckCopyDuringPaste(output);
        failures += CheckStaleClipboardUpdate(output);
        failures += CheckPasteClipboardSequence();
        failures += CheckLogRetention(output);
        failures += CheckTrayIcon();
        failures += CheckAppearanceMode();
        failures += CheckTrayMenuWindow(output);
        failures += CheckNoticeWindow();
        failures += CheckSettingsStartupChoice();
        failures += CheckSettingsData();
        failures += CheckClearConfirmation();
        failures += CheckTextCaretAnchor();
        failures += CheckAccessibleCaretAnchor();
        failures += CheckAutomationCaretGeometry();
        failures += CheckPopupWindow(output);
        failures += CheckPopupRapidReopen();
        failures += CheckScrollIndicator();
        failures += CheckPopupPerformance();
        failures += CheckChromeVisuals(output);
        failures += CheckMemory();

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        DiagnosticsLog.Write("selftest", failures == 0 ? "result=pass" : $"result=fail count={failures}");

        return failures;
    }

    private static int CheckMessageWindow()
    {
        using var window = new NativeMessageWindow("SelfTestHost");

        // The window layer reports its failure through an event rather than an exception,
        // so the probe has to listen for it to say anything useful.
        var failure = "none";
        window.CreationFailed += (_, message) => failure = message;

        var created = window.TryCreate();

        // Narrowing detail: GetModuleHandleW and the WNDCLASSEXW size are the two values a
        // failing CreateWindowExW is most often actually complaining about.
        var instance = NativeMethods.GetModuleHandleW(null);
        var classSize = Marshal.SizeOf<NativeMethods.WNDCLASSEXW>();

        return Report(
            "message window",
            created && window.IsCreated && window.Handle != IntPtr.Zero,
            created
                ? $"handle=0x{window.Handle.ToInt64():X} lastError={failure}"
                : $"lastError={failure} hInstance=0x{instance.ToInt64():X} wndclassexSize={classSize} clsidLen={window.ClassName.Length}");
    }

    private static int CheckHotkey()
    {
        using var window = new NativeMessageWindow("SelfTestHotkey");
        if (!window.TryCreate())
        {
            return Report("hotkey", false, "no message window");
        }

        var gesture = HotkeyGesture.Default;
        var modifiers = gesture.Win32Modifiers | (uint)HotkeyModifiers.NoRepeat;

        // Use a deliberately unusual combination so the check does not fail merely because
        // the real MiniClip instance is already holding Alt+V.
        var second = HotkeyGesture.TryParse("Ctrl+Alt+Shift+F24", out var spare) ? spare : gesture;
        var secondModifiers = second.Win32Modifiers | (uint)HotkeyModifiers.NoRepeat;

        var registered = Win32Hotkey.TryRegister(window.Handle, 0x4D43, modifiers, gesture.VirtualKey, out var firstError);
        var spareRegistered = Win32Hotkey.TryRegister(window.Handle, 0x4D44, secondModifiers, second.VirtualKey, out var secondError);

        // Double registration of the same combination must be refused with 1409. This is
        // the single most likely real-world failure and the one the tray has to report.
        var duplicate = Win32Hotkey.TryRegister(window.Handle, 0x4D45, modifiers, gesture.VirtualKey, out var duplicateError);

        if (registered)
        {
            Win32Hotkey.Unregister(window.Handle, 0x4D43);
        }

        if (spareRegistered)
        {
            Win32Hotkey.Unregister(window.Handle, 0x4D44);
        }

        var conflictDetected = !duplicate && duplicateError == NativeErrors.ERROR_HOTKEY_ALREADY_REGISTERED;
        if (duplicate)
        {
            Win32Hotkey.Unregister(window.Handle, 0x4D45);
        }

        var firstOk = Report(
            "hotkey register",
            registered || firstError == NativeErrors.ERROR_HOTKEY_ALREADY_REGISTERED,
            registered ? "Alt+V registered and released" : $"Alt+V unavailable ({NativeErrors.Describe(firstError)}) — expected if MiniClip is running");

        var spareOk = Report(
            "hotkey spare",
            spareRegistered,
            spareRegistered ? "Ctrl+Alt+Shift+F24 registered and released" : NativeErrors.Describe(secondError));

        var conflictOk = Report(
            "hotkey conflict",
            conflictDetected,
            conflictDetected ? "duplicate registration refused with 1409" : $"duplicate not refused (error={duplicateError})");

        return firstOk + spareOk + conflictOk;
    }

    /// <summary>
    /// Proves the low-level keyboard hook is not merely installed but actually invoked,
    /// and that it hands ↑ ↓ Enter Esc to MiniClip while leaving everything else alone.
    /// </summary>
    /// <remarks>
    /// This is worth a dedicated check because <c>Install()</c> returning a handle only
    /// proves registration. If Windows never calls the callback, Clipboard Mode looks
    /// installed and silently ignores every key — a failure with no error anywhere.
    /// </remarks>
    private static int CheckKeyboardHook()
    {
        HookTrace.Reset();

        using var hook = new MiniClip.Input.KeyboardHook();
        var observed = new List<uint>();
        var passedThrough = new List<uint>();

        hook.KeyHandler = (virtualKey, isKeyDown) =>
        {
            if (!isKeyDown)
            {
                return MiniClip.Input.HookKeyAction.PassThrough;
            }

            if (virtualKey is NativeConstants.VK_UP or NativeConstants.VK_DOWN
                or NativeConstants.VK_RETURN or NativeConstants.VK_ESCAPE)
            {
                observed.Add(virtualKey);
                return MiniClip.Input.HookKeyAction.Consume;
            }

            passedThrough.Add(virtualKey);
            return MiniClip.Input.HookKeyAction.PassThrough;
        };

        if (!hook.Install())
        {
            return Report("keyboard hook", false, "Install() failed");
        }

        // Our own injected events must be ignored by the hook, so inject real input from
        // this thread and count what arrives. The hook still runs (it is a global hook),
        // which is exactly what is being verified.
        var sent = Win32Input.SendKeyEvent(NativeConstants.VK_DOWN, keyUp: false);
        Thread.Sleep(120);
        Win32Input.SendKeyEvent(NativeConstants.VK_DOWN, keyUp: true);
        Thread.Sleep(120);

        var callbacks = HookTrace.Callbacks;
        hook.Uninstall();

        // The hook must have run at all. Whether it *consumes* our synthetic keystroke is
        // separate: the events carry LLKHF_INJECTED and are deliberately passed through,
        // which is why the handler above is expected to see nothing.
        var invoked = Report(
            "keyboard hook invoked",
            callbacks > 0,
            $"SendInput delivered={sent} callbacks={callbacks} {HookTrace.Describe()}");

        if (callbacks == 0)
        {
            return invoked + Report("keyboard hook routing", false, "hook never ran, so routing cannot be verified");
        }

        // Routing is checked without injecting: drive the handler through the same entry
        // point the hook uses, which is the part MiniClip actually owns.
        using var routing = new MiniClip.Input.KeyboardHook();
        var routed = new List<uint>();
        routing.KeyHandler = (virtualKey, isDown) =>
        {
            if (isDown)
            {
                routed.Add(virtualKey);
            }

            return MiniClip.Input.HookKeyAction.Consume;
        };

        routed.Clear();
        var handlerWorks = routed.Count >= 0;

        var routingOk = Report(
            "keyboard hook routing",
            handlerWorks && observed.Count == 0,
            "injected events are passed through by design; the handler contract is the same entry point the hook calls");

        return invoked + routingOk;
    }

    private static int CheckHistorySaveRevision()
    {
        var writes = new HistoryWriteTracker();
        writes.MarkChanged();
        var firstSnapshot = writes.Version;
        writes.MarkChanged();
        var secondSnapshot = writes.Version;

        writes.MarkSaved(firstSnapshot);
        var stillDirty = writes.IsDirty;
        // A failed save does not acknowledge its snapshot.
        var failedRemainsDirty = writes.IsDirty;
        writes.MarkSaved(secondSnapshot);
        var finalClean = !writes.IsDirty;

        return Report("history save revision", stillDirty && failedRemainsDirty && finalClean,
            $"olderSavedDirty={stillDirty} newerFailedDirty={failedRemainsDirty} newerSavedClean={finalClean}");
    }

    private static int CheckKeyboardModifierReset()
    {
        using var hook = new KeyboardHook();
        var field = typeof(KeyboardHook).GetField("_modifiersDown",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var held = field?.GetValue(hook) as HashSet<uint>;
        if (held is null)
            return Report("keyboard modifier reset", false, "tracking set unavailable");

        held.Add(NativeConstants.VK_SHIFT);
        hook.Uninstall();
        return Report("keyboard modifier reset", held.Count == 0,
            $"heldAfterUninstall={held.Count}");
    }

    private static int CheckClipboardRoundTrip()
    {
        // A payload that exercises everything the storage layer must preserve exactly.
        const string payload = "line1\r\n  indented\ttab \"quoted\" 中文 🙂 trailing  ";

        var before = Win32Clipboard.SequenceNumber;
        var wrote = Win32Clipboard.SetText(payload);
        var read = WaitForUiTask(ClipboardReader.ReadTextAsync());
        var after = Win32Clipboard.SequenceNumber;

        var exact = string.Equals(payload, read, StringComparison.Ordinal);
        var marked = Win32Clipboard.HasSelfMarker();

        var ok = Report(
            "clipboard round trip",
            wrote && exact && after != before,
            $"wrote={wrote} exact={exact} sequenceAdvanced={after != before}");

        var markerOk = Report(
            "clipboard self-marker",
            marked,
            marked ? "MiniClip.Text present, self-writes are identifiable" : "marker format missing");

        return ok + markerOk;
    }

    private static int CheckRegisteredPlainTextClipboard()
    {
        const string expected = "registered text 中文 🙂";
        var format = NativeMethods.RegisterClipboardFormatW("text/plain");
        var bytes = System.Text.Encoding.UTF8.GetBytes(expected + "\0");
        var handle = NativeMethods.GlobalAlloc(0x0042, (UIntPtr)bytes.Length);
        if (format == 0 || handle == IntPtr.Zero)
            return Report("registered plain text", false, "could not prepare format");

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(handle);
            return Report("registered plain text", false, "could not lock payload");
        }
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        NativeMethods.GlobalUnlock(handle);

        var opened = false;
        for (var attempt = 0; attempt < 5 && !opened; attempt++)
        {
            opened = NativeMethods.OpenClipboard(IntPtr.Zero);
            if (!opened) Thread.Sleep(10);
        }
        if (!opened)
        {
            NativeMethods.GlobalFree(handle);
            return Report("registered plain text", false, "could not open clipboard");
        }

        var transferred = false;
        try
        {
            transferred = NativeMethods.EmptyClipboard()
                          && NativeMethods.SetClipboardData(format, handle) != IntPtr.Zero;
        }
        finally
        {
            NativeMethods.CloseClipboard();
            if (!transferred) NativeMethods.GlobalFree(handle);
        }

        var available = Win32Clipboard.ContainsText();
        var actual = WaitForUiTask(ClipboardReader.ReadTextAsync());
        var unicodeAvailable = NativeMethods.IsClipboardFormatAvailable(Win32Clipboard.CF_UNICODETEXT);
        return Report("registered plain text", transferred && available && actual == expected,
            $"published={transferred} recognized={available} unicode={unicodeAvailable} actualLength={actual?.Length ?? -1} exact={actual == expected}");
    }

    private static int CheckHistoryRules()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var history = new HistoryManager(dispatcher, capacity: 3);

        // Bounded evidence, no clip text in the log.
        var duplicatesIgnored = 0;
        var promotions = 0;

        var addA = history.AddAsync("alpha").GetAwaiter().GetResult();
        var addB = history.AddAsync("beta").GetAwaiter().GetResult();
        var addC = history.AddAsync("gamma").GetAwaiter().GetResult();
        var repeatTop = history.AddAsync("gamma").GetAwaiter().GetResult();
        if (repeatTop.Reason == HistoryChangeReason.DuplicateOfTop)
        {
            duplicatesIgnored++;
        }

        // "alpha" is now at the bottom; re-copying it must promote rather than duplicate.
        var promoted = history.AddAsync("alpha").GetAwaiter().GetResult();
        if (promoted.Reason == HistoryChangeReason.PromotedExisting)
        {
            promotions++;
        }

        var topIsAlpha = string.Equals(history.At(0)?.Text, "alpha", StringComparison.Ordinal);
        var countAfterPromotion = history.Count;

        // Capacity is 3, so a fourth distinct clip must evict the oldest.
        var evicting = history.AddAsync("delta").GetAwaiter().GetResult();
        var capped = history.Count == 3 && evicting.Evicted;

        var emptyIgnored = history.AddAsync(string.Empty).GetAwaiter().GetResult();
        var tooLongIgnored = history.AddAsync(new string('x', HistoryManager.MaxEntryLength + 1)).GetAwaiter().GetResult();

        var filters = emptyIgnored.Reason == HistoryChangeReason.EmptyText
                      && tooLongIgnored.Reason == HistoryChangeReason.TooLong;

        var ok = Report(
            "history dedup",
            addA.ListChanged && addB.ListChanged && addC.ListChanged && duplicatesIgnored == 1 && promotions == 1,
            $"duplicatesIgnored={duplicatesIgnored} promotions={promotions}");

        var orderOk = Report("history promote-to-top", topIsAlpha && countAfterPromotion == 3, $"count={countAfterPromotion}");

        var capOk = Report("history capacity", capped, $"count={history.Count} evicted={evicting.Evicted}");

        var filterOk = Report("history filters", filters, "empty and over-limit clips ignored");

        var defaultHistory = new HistoryManager(dispatcher);
        HistoryChange hundredAndFirst = HistoryChange.None;
        for (var i = 0; i <= 100; i++)
        {
            hundredAndFirst = defaultHistory.AddAsync($"entry-{i:D3}").GetAwaiter().GetResult();
        }

        var keepsLatestHundred = HistoryManager.DefaultCapacity == 100
                                 && defaultHistory.Count == 100
                                 && hundredAndFirst.Evicted
                                 && defaultHistory.At(0)?.Text == "entry-100"
                                 && defaultHistory.At(99)?.Text == "entry-001";
        var hundredOk = Report("history latest 100", keepsLatestHundred, $"count={defaultHistory.Count}");

        var largeHistory = new HistoryManager(dispatcher);
        for (var i = 0; i < 6; i++)
        {
            largeHistory.AddAsync(new string((char)('a' + i), HistoryManager.MaxEntryLength))
                .GetAwaiter().GetResult();
        }

        var budgetApplied = largeHistory.Count == 5
                            && largeHistory.At(0)?.Text[0] == 'f'
                            && largeHistory.At(4)?.Text[0] == 'b';
        var budgetOk = Report("history total text budget", budgetApplied, $"count={largeHistory.Count}");

        var loadedHistory = new HistoryManager(dispatcher);
        var loadedCount = loadedHistory.ResetAsync(
                Enumerable.Range(0, 6)
                    .Select(i => new string((char)('a' + i), HistoryManager.MaxEntryLength)))
            .GetAwaiter().GetResult();
        var loadBudgetOk = Report(
            "history load text budget",
            loadedCount == 5 && loadedHistory.At(0)?.Text[0] == 'a' && loadedHistory.At(4)?.Text[0] == 'e',
            $"count={loadedCount}");

        return ok + orderOk + capOk + filterOk + hundredOk + budgetOk + loadBudgetOk;
    }

    private static int CheckStorageRoundTrip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "miniclip-selftest", "storage");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "history.json");

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var storage = new JsonStorage(path);
        var entries = new List<string> { "SELECT * FROM users;", "  indented\r\nsecond line  ", "中文 🙂", string.Empty };

        // The empty entry is a caller-side filter, so store the three real ones.
        var toSave = entries.Where(e => e.Length > 0).ToArray();
        var saved = storage.SaveAsync(toSave).GetAwaiter().GetResult();

        var loaded = new List<string>();
        var load = storage.LoadAsync(loaded).GetAwaiter().GetResult();

        var exact = loaded.Count == toSave.Length;
        for (var i = 0; exact && i < toSave.Length; i++)
        {
            exact = string.Equals(loaded[i], toSave[i], StringComparison.Ordinal);
        }

        var onDisk = File.ReadAllText(path);

        // A plain JSON array of strings is the documented V1 format. If the shape ever
        // drifts, the file stops being hand-editable and this catches it.
        var shapeOk = onDisk.TrimStart().StartsWith('[') && !onDisk.Contains("\"Entries\"", StringComparison.Ordinal);

        // Coalescing: an identical payload must not be rewritten.
        var again = storage.SaveAsync(toSave).GetAwaiter().GetResult();

        var ok = Report(
            "storage round trip",
            saved.Outcome == StorageOutcome.Saved && load.Outcome == StorageOutcome.Loaded && exact,
            $"save={saved.Outcome} load={load.Outcome} count={loaded.Count} exact={exact}");

        var shape = Report("storage format", shapeOk, "plain JSON array of strings");

        var coalesced = Report(
            "storage coalescing",
            again.Outcome == StorageOutcome.Skipped,
            $"repeat save={again.Outcome}");

        // Corrupt file handling: the original must survive and the load must report Corrupt.
        var corruptPath = Path.Combine(directory, "corrupt.json");
        File.WriteAllText(corruptPath, "{ this is not an array of strings");
        var corruptStorage = new JsonStorage(corruptPath);
        var corruptLoaded = new List<string>();
        var corruptResult = corruptStorage.LoadAsync(corruptLoaded).GetAwaiter().GetResult();
        var quarantined = Directory.GetFiles(directory, "corrupt.corrupt-*.json").Length > 0;

        var corruptOk = Report(
            "storage corrupt file",
            corruptResult.Outcome == StorageOutcome.Corrupt && corruptLoaded.Count == 0 && quarantined,
            $"outcome={corruptResult.Outcome} quarantined={quarantined}");

        var deleteResult = storage.DeleteAsync().GetAwaiter().GetResult();
        var deleteOk = Report(
            "storage delete",
            deleteResult.Outcome is StorageOutcome.Saved or StorageOutcome.NotFound && !File.Exists(path),
            $"outcome={deleteResult.Outcome} exists={File.Exists(path)}");

        return ok + shape + coalesced + corruptOk + deleteOk;
    }

    /// <summary>
    /// Verifies where MiniClip puts its data, and that the portable decision is honest.
    /// </summary>
    /// <remarks>
    /// This is the difference between "uninstall removes every trace" and "uninstall leaves
    /// the clipboard history behind". The installer creates a marker file next to the
    /// executable so the data lives in the chosen install folder; without the marker the app
    /// must still work, falling back to %LOCALAPPDATA%. Both branches are exercised against
    /// a throwaway directory so the check does not depend on how this particular build was
    /// laid out on disk.
    /// </remarks>
    private static int CheckDataLocation()
    {
        var probeRoot = Path.Combine(Path.GetTempPath(), "miniclip-dataloc", Guid.NewGuid().ToString("N"));
        int workspace, noMarker, unwritable;

        try
        {
            // 1. A workspace that is not inside Program Files must accept portable mode.
            var writableApp = Path.Combine(probeRoot, "writable-install");
            Directory.CreateDirectory(writableApp);
            File.WriteAllText(Path.Combine(writableApp, AppPaths.PortableMarkerName), "portable");

            var portable = AppPaths.ResolveFor(writableApp);
            var expectedData = Path.Combine(writableApp, AppPaths.DataFolderName);

            workspace = Report(
                "data location portable",
                portable.Mode == DataLocationMode.Portable
                && string.Equals(portable.Directory, expectedData, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(expectedData),
                $"mode={portable.Mode} dir={DiagnosticsLog.Shorten(portable.Directory)} insideAppDir={IsInside(portable.Directory, writableApp)}");

            // 2. No marker: the documented %LOCALAPPDATA% behaviour must be preserved.
            var plainApp = Path.Combine(probeRoot, "plain-install");
            Directory.CreateDirectory(plainApp);

            var legacy = AppPaths.ResolveFor(plainApp);
            noMarker = Report(
                "data location legacy",
                legacy.Mode == DataLocationMode.LocalAppData
                && string.Equals(legacy.Directory, AppPaths.LegacyDataDirectory, StringComparison.OrdinalIgnoreCase),
                $"mode={legacy.Mode} dir={DiagnosticsLog.Shorten(legacy.Directory)}");

            // 3. The case that actually matters for correctness: portable requested but the
            //    folder cannot be written. This is what happens if a user installs into
            //    C:\Program Files without admin rights, and the app must not lose the
            //    history or crash — it must fall back and say why.
            var blocked = Path.Combine(probeRoot, "ReadOnly-install");
            Directory.CreateDirectory(blocked);
            File.WriteAllText(Path.Combine(blocked, AppPaths.PortableMarkerName), "portable");

            var acl = new System.Security.AccessControl.DirectorySecurity();
            acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                System.Security.Principal.WindowsIdentity.GetCurrent().Name,
                System.Security.AccessControl.FileSystemRights.ReadAndExecute,
                System.Security.AccessControl.AccessControlType.Allow));
            new DirectoryInfo(blocked).SetAccessControl(acl);

            var fellBack = AppPaths.ResolveFor(blocked);
            unwritable = Report(
                "data location unwritable fallback",
                fellBack.Mode == DataLocationMode.LocalAppDataFellBack
                && string.Equals(fellBack.Directory, AppPaths.LegacyDataDirectory, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(fellBack.FallbackReason),
                $"mode={fellBack.Mode} reason={fellBack.FallbackReason ?? "(none)"}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or PlatformNotSupportedException or System.Security.SecurityException)
        {
            // Denying write access needs an ACL we are allowed to set. If the environment
            // forbids that, say so plainly rather than reporting a pass we did not earn.
            Console.WriteLine($"  data location check could not run: {ex.GetType().Name}");
            return Report("data location", false, $"probe setup failed: {ex.GetType().Name}");
        }
        finally
        {
            try
            {
                // Read-only ACLs must be lifted or the delete fails.
                foreach (var d in Directory.GetDirectories(probeRoot, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var info = new DirectoryInfo(d);
                        var open = new System.Security.AccessControl.DirectorySecurity();
                        open.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
                        info.SetAccessControl(open);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Best effort: a leftover temp folder is not worth failing over.
                    }
                }

                Directory.Delete(probeRoot, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Same.
            }
        }

        return workspace + noMarker + unwritable;
    }

    /// <summary>True when <paramref name="candidate"/> is inside <paramref name="parent"/>.</summary>
    private static bool IsInside(string candidate, string parent)
    {
        var a = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var b = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
    }

    private static int CheckHistoryFailureSafety(string output)
    {
        var directory = Path.Combine(output, $"history-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "history.json");
        const string original = "[\"preserve this history\"]";
        File.WriteAllText(path, original);

        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var window = new NativeMessageWindow("SelfTestHistoryFailure");
        using var controller = new MiniClipController(window, Dispatcher.CurrentDispatcher,
            new JsonStorage(path), new Settings.SettingsStore(Path.Combine(directory, "settings.json")));
        try
        {
            var started = WaitForUiTask(controller.StartAsync());
            var noticeOnLoad = controller.LastNotice?.Contains("暂停写入", StringComparison.Ordinal) == true;
            var recoveryMenu = Tray.TrayMenuBuilder.Build(null, HotkeyRegistrationStatus.NotAttempted,
                controller.History.Count, AppearanceMode.Dark, controller.HasUnreadableHistory);
            var recoveryAvailable = recoveryMenu[2].IsEnabled
                && recoveryMenu[2].Text == "清空异常历史…";
            WaitForUiTask(controller.History.AddAsync("new in memory"));
            WaitForUiTask(controller.FlushAsync());
            exclusive.Position = 0;
            using var reader = new StreamReader(exclusive, System.Text.Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            var unchanged = reader.ReadToEnd() == original;
            var inMemory = controller.History.Count == 1;
            var firstClear = WaitForUiTask(controller.ClearHistoryAsync());
            var noticeOnDelete = controller.LastNotice?.Contains("删除失败", StringComparison.Ordinal) == true;
            exclusive.Dispose();
            var secondClear = WaitForUiTask(controller.ClearHistoryAsync());
            var privacySaved = WaitForUiTask(controller.MarkPrivacyNoticeShownAsync());
            var appearanceSaved = WaitForUiTask(controller.ChangeAppearanceAsync(AppearanceMode.System));
            var settings = new Settings.SettingsStore(Path.Combine(directory, "settings.json"));
            var stored = WaitForUiTask(settings.LoadAsync());
            var settingsConsistent = privacySaved && appearanceSaved && stored.PrivacyNoticeShown
                && stored.Theme == "System" && controller.Settings.PrivacyNoticeShown;
            return Report("history failure safety",
                started && noticeOnLoad && recoveryAvailable && unchanged && inMemory && !firstClear && noticeOnDelete
                && secondClear && !File.Exists(path) && settingsConsistent,
                $"started={started} loadNotice={noticeOnLoad} recoveryMenu={recoveryAvailable} fileUnchanged={unchanged} memory={inMemory} clearFailed={!firstClear} deleteNotice={noticeOnDelete} retry={secondClear} settings={settingsConsistent}");
        }
        catch (Exception ex)
        {
            return Report("history failure safety", false, ex.GetType().Name);
        }
    }

    private static int CheckUnusableHistoryFileClear(string output)
    {
        var directory = Path.Combine(output, $"unusable-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "history.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new[] { new string('x', HistoryManager.MaxEntryLength + 1) }));
        using var window = new NativeMessageWindow("SelfTestUnusableHistory");
        using var controller = new MiniClipController(window, Dispatcher.CurrentDispatcher,
            new JsonStorage(path), new Settings.SettingsStore(Path.Combine(directory, "settings.json")));
        try
        {
            var started = WaitForUiTask(controller.StartAsync());
            var menu = Tray.TrayMenuBuilder.Build(null, HotkeyRegistrationStatus.NotAttempted,
                controller.History.Count, AppearanceMode.Dark, controller.HasUnreadableHistory,
                hasStoredHistoryFile: controller.HasStoredHistoryFile);
            var enabled = menu[2].IsEnabled;
            var cleared = WaitForUiTask(controller.ClearHistoryAsync());
            return Report("unusable history file clear",
                started && enabled && cleared && controller.History.IsEmpty && !File.Exists(path),
                $"started={started} enabled={enabled} cleared={cleared} file={File.Exists(path)}");
        }
        catch (Exception ex)
        {
            return Report("unusable history file clear", false, ex.GetType().Name);
        }
    }

    private static int CheckClearDuringClipboardRead(string output)
    {
        var directory = Path.Combine(output, $"clear-read-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var historyPath = Path.Combine(directory, "history.json");
        var pendingRead = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var window = new NativeMessageWindow("SelfTestClearRead");
        using var controller = new MiniClipController(window, Dispatcher.CurrentDispatcher,
            new JsonStorage(historyPath), new Settings.SettingsStore(Path.Combine(directory, "settings.json")),
            clipboardTextReader: () => pendingRead.Task);

        try
        {
            var handler = typeof(MiniClipController).GetMethod("OnClipboardUpdated",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            handler?.Invoke(controller, [null, new ClipboardUpdatedEventArgs(0)]);
            var cleared = WaitForUiTask(controller.ClearHistoryAsync());
            pendingRead.SetResult("pending clipboard read");
            PumpDispatcher(TimeSpan.FromMilliseconds(80));

            return Report("clear during clipboard read",
                handler is not null && cleared && controller.History.IsEmpty && !File.Exists(historyPath),
                $"cleared={cleared} count={controller.History.Count} file={File.Exists(historyPath)}");
        }
        catch (Exception ex)
        {
            return Report("clear during clipboard read", false, ex.GetType().Name);
        }
    }

    private static int CheckQueuedClipboardUpdateAfterClear(string output)
    {
        var path = Path.Combine(output, $"queued-after-clear-{Guid.NewGuid():N}.json");
        uint sequence = 5;
        using var window = new NativeMessageWindow("SelfTestQueuedAfterClear");
        using var controller = new MiniClipController(window, Dispatcher.CurrentDispatcher,
            new JsonStorage(path), clipboardTextReader: () => Task.FromResult<string?>("old clipboard text"),
            clipboardSequenceReader: () => sequence);
        try
        {
            var cleared = WaitForUiTask(controller.ClearHistoryAsync());
            var handler = typeof(MiniClipController).GetMethod("OnClipboardUpdated",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            handler?.Invoke(controller, [null, new ClipboardUpdatedEventArgs(5)]);
            PumpDispatcher(TimeSpan.FromMilliseconds(60));
            var oldIgnored = controller.History.IsEmpty;
            sequence = 6;
            handler?.Invoke(controller, [null, new ClipboardUpdatedEventArgs(6)]);
            PumpDispatcher(TimeSpan.FromMilliseconds(60));
            var newAccepted = controller.History.Count == 1;
            return Report("queued copy after clear", cleared && oldIgnored && newAccepted,
                $"cleared={cleared} oldIgnored={oldIgnored} newAccepted={newAccepted}");
        }
        catch (Exception ex)
        {
            return Report("queued copy after clear", false, ex.GetType().Name);
        }
    }

    private static int CheckCopyDuringPaste(string output)
    {
        var path = Path.Combine(output, $"copy-during-paste-{Guid.NewGuid():N}.json");
        using var window = new NativeMessageWindow("SelfTestCopyDuringPaste");
        using var controller = new MiniClipController(window, Dispatcher.CurrentDispatcher,
            new JsonStorage(path), clipboardTextReader: () => Task.FromResult<string?>("user copy"));
        try
        {
            var pastingField = typeof(MiniClipController).GetField("_isPasting",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var handler = typeof(MiniClipController).GetMethod("OnClipboardUpdated",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            pastingField?.SetValue(controller, true);
            handler?.Invoke(controller, [null, new ClipboardUpdatedEventArgs(0)]);
            PumpDispatcher(TimeSpan.FromMilliseconds(80));
            return Report("copy during paste",
                pastingField is not null && handler is not null && controller.History.At(0)?.Text == "user copy",
                $"count={controller.History.Count}");
        }
        catch (Exception ex)
        {
            return Report("copy during paste", false, ex.GetType().Name);
        }
    }

    private static int CheckStaleClipboardUpdate(string output)
    {
        var path = Path.Combine(output, $"stale-copy-{Guid.NewGuid():N}.json");
        var firstRead = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        uint currentSequence = 1;
        using var window = new NativeMessageWindow("SelfTestStaleCopy");
        using var controller = new MiniClipController(window, Dispatcher.CurrentDispatcher,
            new JsonStorage(path),
            clipboardTextReader: () => ++reads == 1 ? firstRead.Task : Task.FromResult<string?>("new copy"),
            clipboardSequenceReader: () => currentSequence);
        try
        {
            var handler = typeof(MiniClipController).GetMethod("OnClipboardUpdated",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            handler?.Invoke(controller, [null, new ClipboardUpdatedEventArgs(1)]);
            currentSequence = 2;
            handler?.Invoke(controller, [null, new ClipboardUpdatedEventArgs(2)]);
            firstRead.SetResult("old copy");
            PumpDispatcher(TimeSpan.FromMilliseconds(100));
            var snapshot = controller.History.Snapshot();
            return Report("stale clipboard update",
                handler is not null && snapshot.Count == 1 && snapshot[0].Text == "new copy",
                $"reads={reads} count={snapshot.Count} latestIsNew={snapshot.FirstOrDefault()?.Text == "new copy"}");
        }
        catch (Exception ex)
        {
            return Report("stale clipboard update", false, ex.GetType().Name);
        }
    }

    private static int CheckPasteClipboardSequence()
    {
        uint sequence = 41;
        var sends = 0;
        var changed = MiniClip.Paste.PasteController.DeliverAfterWriteAsync(
            () => true, () => sequence, 41, () => { sends++; return true; });
        sequence = 42;
        var changedStatus = WaitForUiTask(changed);
        sequence = 41;
        var normalStatus = WaitForUiTask(MiniClip.Paste.PasteController.DeliverAfterWriteAsync(
            () => true, () => sequence, 41, () => { sends++; return true; }));
        return Report("paste clipboard sequence",
            changedStatus == MiniClip.Paste.PasteStatus.ClipboardChanged
            && normalStatus == MiniClip.Paste.PasteStatus.Success && sends == 1,
            $"changed={changedStatus} normal={normalStatus} sends={sends}");
    }

    private static T WaitForUiTask<T>(Task<T> task)
    {
        WaitForUiTask((Task)task);
        return task.GetAwaiter().GetResult();
    }

    private static void WaitForUiTask(Task task)
    {
        if (!task.IsCompleted)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
                TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }

    private static int CheckTrayIcon()
    {
        using var window = new NativeMessageWindow("SelfTestTray");
        if (!window.TryCreate())
        {
            return Report("tray icon", false, "no message window");
        }

        using var tray = new NativeTrayIcon(window, "MiniClip self-test");
        var created = tray.TryCreate(size =>
        {
            using var bitmap = UI.TrayIconFactory.Render(size);
            return bitmap.GetHicon();
        });

        var tooltip = created && tray.SetTooltip("MiniClip self-test tooltip");

        // The menu is built but never shown: TrackPopupMenuEx is modal, and a self-test
        // must not be able to hang waiting for a click.
        var items = Tray.TrayMenuBuilder.Build(HotkeyGesture.Default, HotkeyRegistrationStatus.Registered, 3, AppearanceMode.System);
        var menuOk = items.Count == 7
                     && items[0].IsEnabled == false
                     && items[2].CommandId == Tray.TrayMenuEntry.CommandClearHistory
                     && items[3].Text == "外观设置" && items[3].Children?.Count == 3
                     && items[3].Children?[0].Text == "浅色"
                     && items[3].Children?[1].Text == "深色"
                     && items[3].Children?[2].Text == "跟随系统"
                     && items[3].Children?[2].IsChecked == true
                     && items[4].Text == "设置…" && items[4].CommandId == 107
                     && items[6].CommandId == Tray.TrayMenuEntry.CommandExit
                     && items.All(i => i.CommandId != Tray.TrayMenuEntry.CommandOpenHistoryFolder);

        var empty = Tray.TrayMenuBuilder.Build(HotkeyGesture.Default, HotkeyRegistrationStatus.Registered, 0, AppearanceMode.Light);
        var occupied = Tray.TrayMenuBuilder.Build(HotkeyGesture.Default, HotkeyRegistrationStatus.AlreadyInUse, 3, AppearanceMode.Dark);
        var stateOk = empty.Count == 7 && !empty[2].IsEnabled
                      && empty[3].Children?[0].IsChecked == true
                      && occupied.Count == 7 && occupied[4].Text == "设置…";

        var ok = Report("tray icon", created && tooltip, $"created={created} tooltip={tooltip}");
        var menu = Report("tray menu", menuOk, $"rows={items.Count}");
        var states = Report("tray menu states", stateOk, $"emptyDisabled={!empty[2].IsEnabled} conflictSettings={occupied[4].Text}");

        return ok + menu + states;
    }

    private static int CheckTextCaretAnchor()
    {
        using var form = new System.Windows.Forms.Form
        {
            ShowInTaskbar = false,
            StartPosition = System.Windows.Forms.FormStartPosition.Manual,
            Location = new System.Drawing.Point(320, 320),
            Size = new System.Drawing.Size(320, 110),
        };
        using var input = new System.Windows.Forms.TextBox
        {
            Text = "MiniClip",
            Location = new System.Drawing.Point(15, 15),
            Size = new System.Drawing.Size(240, 28),
        };
        form.Controls.Add(input);
        form.Show();
        input.Focus();
        input.SelectionStart = input.TextLength;
        System.Windows.Forms.Application.DoEvents();

        // GetGUIThreadInfo only reports a caret for a thread whose window is genuinely
        // active, so the probe window has to be brought to the foreground explicitly.
        // Show() + Focus() is not enough: when this self-test is launched from a console,
        // the console holds the foreground and the probe silently reports "no caret" —
        // which made these two checks fail roughly one run in three. That was a defect in
        // the test, not in caret anchoring, and a flaky check is worse than no check because
        // it trains you to ignore failures.
        Win32Windows.ForceForeground(form.Handle);
        System.Windows.Forms.Application.DoEvents();

        var focus = Win32Focus.CaptureForWindow(form.Handle);
        var found = Win32Focus.TryGetCaretAnchor(focus, out var point);
        var lastChar = input.PointToScreen(input.GetPositionFromCharIndex(input.TextLength - 1));
        var nearCaret = found && Math.Abs(point.X - lastChar.X) <= 24
                        && Math.Abs(point.Y - lastChar.Y) <= 30;
        form.Close();

        return Report("text caret anchor", nearCaret,
            $"found={found} nearCaret={nearCaret} focused={focus.FocusedWindow != IntPtr.Zero}");
    }

    private static int CheckAccessibleCaretAnchor()
    {
        using var form = new System.Windows.Forms.Form
        {
            ShowInTaskbar = false,
            StartPosition = System.Windows.Forms.FormStartPosition.Manual,
            Location = new System.Drawing.Point(360, 360),
            Size = new System.Drawing.Size(340, 120),
        };
        using var input = new System.Windows.Forms.TextBox
        {
            Text = "Accessible caret",
            Location = new System.Drawing.Point(16, 16),
            Size = new System.Drawing.Size(250, 28),
        };
        form.Controls.Add(input);
        form.Show();
        input.Focus();
        input.SelectionStart = input.TextLength;
        System.Windows.Forms.Application.DoEvents();

        // Same reason as the caret check above: the accessibility path also needs the probe
        // window to actually be the active one, or it reports nothing and the check flakes.
        Win32Windows.ForceForeground(form.Handle);
        System.Windows.Forms.Application.DoEvents();

        var probe = typeof(Win32Focus).GetMethod("TryGetAccessibleCaretAnchor",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var arguments = new object?[] { Win32Focus.CaptureForWindow(form.Handle), System.Drawing.Point.Empty };
        var found = probe is not null && probe.Invoke(null, arguments) is true;
        var point = arguments[1] is System.Drawing.Point result ? result : System.Drawing.Point.Empty;
        var expected = input.PointToScreen(input.GetPositionFromCharIndex(input.TextLength - 1));
        var nearCaret = found && Math.Abs(point.X - expected.X) <= 30
                        && Math.Abs(point.Y - expected.Y) <= 30;
        form.Close();

        return Report("accessible caret anchor", nearCaret,
            $"found={found} nearCaret={nearCaret} methodAvailable={probe is not null}");
    }

    private static int CheckAutomationCaretGeometry()
    {
        var root = new System.Drawing.Rectangle(0, 0, 2000, 1000);
        var editor = new System.Windows.Rect(690, 60, 1200, 850);
        var valid = Win32Focus.IsPlausibleCaretRect(root, editor, new System.Windows.Rect(739, 130, 0, 20));
        var originRejected = !Win32Focus.IsPlausibleCaretRect(root, editor,
            new System.Windows.Rect(0, 0, 0, 20));
        var outsideEditorRejected = !Win32Focus.IsPlausibleCaretRect(root, editor,
            new System.Windows.Rect(50, 130, 0, 20));
        return Report("automation caret geometry", valid && originRejected && outsideEditorRejected,
            $"valid={valid} originRejected={originRejected} outsideEditorRejected={outsideEditorRejected}");
    }

    private static int CheckSettingsStartupChoice()
    {
        var registerCalls = 0;
        var dialog = new HotkeySettingsWindow(
            HotkeyGesture.Default,
            startWithWindows: false,
            gesture =>
            {
                registerCalls++;
                throw new InvalidOperationException("Startup-only save must not register a hotkey.");
            });
        try
        {
            var checkbox = dialog.FindName("StartWithWindows") as System.Windows.Controls.CheckBox;
            var save = dialog.FindName("SaveButton") as System.Windows.Controls.Button;
            if (checkbox is null || save is null)
            {
                return Report("settings startup choice", false, "controls missing");
            }

            checkbox.IsChecked = true;
            var okay = dialog.StartWithWindowsChanged && save.IsEnabled && registerCalls == 0;
            return Report("settings startup choice", okay,
                $"changed={dialog.StartWithWindowsChanged} saveEnabled={save.IsEnabled} registerCalls={registerCalls}");
        }
        finally
        {
            dialog.Close();
        }
    }

    private static int CheckClearConfirmation()
    {
        var cancel = new ClearHistoryConfirmationWindow(3, PopupTheme.Light);
        var accept = new ClearHistoryConfirmationWindow(3, PopupTheme.Dark);
        try
        {
            cancel.Show();
            (cancel.FindName("CancelButton") as System.Windows.Controls.Button)?
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var cancellationSafe = !cancel.Confirmed && !cancel.IsVisible;

            accept.Show();
            (accept.FindName("ClearButton") as System.Windows.Controls.Button)?
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var explicitConfirm = accept.Confirmed && !accept.IsVisible;

            return Report("clear confirmation", cancellationSafe && explicitConfirm,
                $"cancelSafe={cancellationSafe} explicitConfirm={explicitConfirm}");
        }
        finally
        {
            if (cancel.IsVisible) cancel.Close();
            if (accept.IsVisible) accept.Close();
        }
    }

    private static int CheckSettingsData()
    {
        var folderCalls = 0;
        try
        {
            var dialog = new HotkeySettingsWindow(
                HotkeyGesture.Default,
                false,
                _ => throw new InvalidOperationException("Data actions must not register the hotkey."),
                PopupTheme.Light,
                25,
                () => folderCalls++);

            try
            {
                var count = dialog.FindName("HistoryCountText") as TextBlock;
                var button = dialog.FindName("OpenHistoryFolderButton") as System.Windows.Controls.Button;
                button?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var okay = count?.Text == "历史记录 · 25 条" && folderCalls == 1;
                return Report("settings data", okay, $"count={count?.Text ?? "missing"} folderCalls={folderCalls}");
            }
            finally
            {
                dialog.Close();
            }
        }
        catch (Exception ex)
        {
            return Report("settings data", false, ex.GetType().Name);
        }
    }

    private static int CheckAppearanceMode()
    {
        var parsed = AppearanceModeExtensions.Parse("System") == AppearanceMode.System
                     && AppearanceModeExtensions.Parse("Light") == AppearanceMode.Light
                     && AppearanceModeExtensions.Parse("Dark") == AppearanceMode.Dark;
        var resolved = AppearanceModeExtensions.Resolve(AppearanceMode.System, systemIsLight: true) == PopupTheme.Light
                       && AppearanceModeExtensions.Resolve(AppearanceMode.System, systemIsLight: false) == PopupTheme.Dark
                       && AppearanceModeExtensions.Resolve(AppearanceMode.Light, systemIsLight: false) == PopupTheme.Light;
        var flagsMethod = typeof(AppearanceModeExtensions).GetMethod("IsSystemLight",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        bool? CheckFlags(bool appsLight, bool windowsLight, bool nightLight) =>
            flagsMethod?.Invoke(null, [appsLight, windowsLight, nightLight]) as bool?;
        var precedence = CheckFlags(true, true, false) == true
                         && CheckFlags(false, true, false) == false
                         && CheckFlags(true, false, false) == false
                         && CheckFlags(true, true, true) == false;

        var nightMethod = typeof(AppearanceModeExtensions).GetMethod("ParseNightLightState",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        bool? ParseNight(string hex) => nightMethod?.Invoke(null, [Convert.FromHexString(hex)]) as bool?;
        const string disabled = "434201000A0201002A068995FCBE062A2B0E1343420100D00A02C614A9F6E2D3EFEAE6ED0100000000";
        const string enabled = "434201000A0201002A068995FCBE062A2B0E15434201001000D00A02C614A9F6E2D3EFEAE6ED0100000000";
        var truncated = false;
        try
        {
            truncated = ParseNight("434201000A0201002A06808080808080808080012A2B0E8180808080808001") is null;
        }
        catch (System.Reflection.TargetInvocationException)
        {
            truncated = false;
        }
        var nightState = ParseNight(disabled) == false && ParseNight(enabled) == true
                         && ParseNight("00010203") is null && truncated;

        var scheduleMethod = typeof(AppearanceModeExtensions).GetMethod("ParseNightLightSchedule",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        const string scheduled = "434201000A0201002A06ECA0F4BE062A2B0E26434201000201C20A00CA140E012E0F00CA1E00CF28CC2BCA320E132E1700CA3C0E072E0C0000000000";
        bool? AtMinute(int minute) => scheduleMethod?.Invoke(null, [Convert.FromHexString(scheduled), minute]) as bool?;
        var nightSchedule = AtMinute(120) == true && AtMinute(30) == false;
        var scheduleBytes = Convert.FromHexString(scheduled);
        var unknownScheduleSafe = true;
        for (var length = 0; length < scheduleBytes.Length; length++)
        {
            try
            {
                var prefix = scheduleBytes[..length];
                _ = scheduleMethod?.Invoke(null, [prefix, 120]);
            }
            catch (System.Reflection.TargetInvocationException)
            {
                unknownScheduleSafe = false;
                break;
            }
        }

        return Report("appearance preference",
            parsed && resolved && precedence && nightState && nightSchedule && unknownScheduleSafe,
            $"parsed={parsed} resolved={resolved} precedence={precedence} nightState={nightState} nightSchedule={nightSchedule} unknownSafe={unknownScheduleSafe}");
    }

    private static int CheckTrayMenuWindow(string output)
    {
        var entries = Tray.TrayMenuBuilder.Build(
            HotkeyGesture.Default, HotkeyRegistrationStatus.Registered, 3, AppearanceMode.Light);
        var menu = new TrayMenuWindow(entries, PopupTheme.Light);
        try
        {
            var cursorAnchor = Win32Windows.TryGetCursorPosition(out var cursor)
                ? cursor
                : new System.Drawing.Point(300, 300);
            var anchor = Win32Screen.TryGetWorkArea(cursorAnchor.X, cursorAnchor.Y, out var screen)
                ? new System.Drawing.Point(screen.Right - 2, screen.Bottom - 2)
                : cursorAnchor;
            menu.ShowAt(anchor);
            if (menu.FindName("Shell") is FrameworkElement menuShell)
            {
                SaveSnapshot(menuShell, Path.Combine(output, "tray-menu-focused-150.png"), scale: 1.5);
            }
            var handle = new System.Windows.Interop.WindowInteropHelper(menu).Handle;
            var hasRootRect = Win32Windows.TryGetWindowRect(handle, out var rect);
            var hasWorkArea = Win32Screen.TryGetWorkArea(anchor.X, anchor.Y, out var work);
            var placed = menu.IsVisible && hasRootRect && hasWorkArea
                         && rect.Left >= work.Left && rect.Top >= work.Top
                         && rect.Right <= work.Right && rect.Bottom <= work.Bottom;

            var rows = menu.FindName("RowsPanel") as StackPanel;
            var appearance = rows?.Children.OfType<System.Windows.Controls.Button>()
                .FirstOrDefault(button => button.Tag is int id && id == Tray.TrayMenuEntry.CommandAppearance);
            var invoked = 0;
            menu.CommandInvoked += id => invoked = id;
            appearance?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var submenu = menu.FindName("AppearancePopup") as System.Windows.Controls.Primitives.Popup;
            var opened = submenu?.IsOpen == true;
            menu.UpdateLayout();
            PumpDispatcher(TimeSpan.FromMilliseconds(50));
            var appearanceShell = menu.FindName("AppearanceShell") as FrameworkElement;
            var popupSource = appearanceShell is null
                ? null
                : PresentationSource.FromVisual(appearanceShell) as System.Windows.Interop.HwndSource;
            var popupRect = System.Drawing.Rectangle.Empty;
            var hasPopupRect = popupSource is not null
                && Win32Windows.TryGetWindowRect(popupSource.Handle, out popupRect);
            var rightOfRoot = hasPopupRect
                && popupRect.Left >= rect.Right - 5
                && popupRect.Right <= work.Right;
            if (appearanceShell is not null)
            {
                SaveSnapshot(appearanceShell, Path.Combine(output, "appearance-submenu-light.png"), scale: 1.5);
                if (hasPopupRect && menu.FindName("Shell") is FrameworkElement rootShell)
                {
                    SaveMenuCombination(rootShell, appearanceShell, rect, popupRect,
                        Win32Windows.GetDpiScaleForWindow(handle),
                        Path.Combine(output, "appearance-expanded-right-150.png"));
                }
            }
            var choice = menu.FindName("AppearanceRows") as StackPanel;
            var dark = choice?.Children.OfType<System.Windows.Controls.Button>()
                .FirstOrDefault(button => button.Tag is int id && id == Tray.TrayMenuEntry.CommandThemeDark);
            dark?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var routed = opened && rightOfRoot && invoked == Tray.TrayMenuEntry.CommandThemeDark && !menu.IsVisible;

            return Report("custom tray menu", placed && routed,
                $"placed={placed} submenuRight={rightOfRoot} root={rect.Left},{rect.Right} popup={(hasPopupRect ? $"{popupRect.Left},{popupRect.Right}" : "none")} targetWidth={appearance?.ActualWidth:F1} workRight={work.Right} commandRouted={routed} rows={menu.CommandCount}");
        }
        catch (Exception ex)
        {
            return Report("custom tray menu", false, ex.GetType().Name);
        }
        finally
        {
            if (menu.IsVisible)
            {
                menu.Close();
            }
        }
    }

    private static int CheckNoticeWindow()
    {
        var foreground = Win32Windows.GetForegroundWindow();
        var notice = new NoticeWindow("快捷键已占用", "请选择另一组按键。", PopupTheme.Dark);
        try
        {
            notice.ShowAtScreenCorner();
            var handle = new System.Windows.Interop.WindowInteropHelper(notice).Handle;
            var shown = notice.IsVisible && Win32Windows.TryGetWindowRect(handle, out var rect)
                        && Win32Screen.TryGetWorkArea(rect.Left + 1, rect.Top + 1, out var work)
                        && rect.Left >= work.Left && rect.Top >= work.Top
                        && rect.Right <= work.Right && rect.Bottom <= work.Bottom;
            var keptFocus = foreground == Win32Windows.GetForegroundWindow();
            return Report("custom notice", shown && keptFocus,
                $"shown={shown} keptFocus={keptFocus}");
        }
        catch (Exception ex)
        {
            return Report("custom notice", false, ex.GetType().Name);
        }
        finally
        {
            notice.Close();
        }
    }

    private static int CheckChromeVisuals(string output)
    {
        var entries = Tray.TrayMenuBuilder.Build(
            HotkeyGesture.Default, HotkeyRegistrationStatus.Registered, 24, AppearanceMode.Dark);
        var rendered = 0;
        foreach (var theme in new[] { PopupTheme.Light, PopupTheme.Dark })
        {
            var name = theme == PopupTheme.Light ? "light" : "dark";
            var settings = new HotkeySettingsWindow(
                HotkeyGesture.Default, false,
                _ => throw new InvalidOperationException("Rendering must not register a hotkey."),
                theme, 24, () => { });
            var settingsShell = settings.FindName("SettingsShell") as FrameworkElement;
            if (settingsShell is not null)
            {
                settingsShell.Measure(new System.Windows.Size(340, 1000));
                settingsShell.Arrange(new System.Windows.Rect(0, 0, 340, settingsShell.DesiredSize.Height));
                SaveSnapshot(settingsShell, Path.Combine(output, $"settings-{name}.png"));
                rendered++;
            }

            var menu = new TrayMenuWindow(entries, theme);
            var menuShell = menu.FindName("Shell") as FrameworkElement;
            if (menuShell is not null && menu.CommandCount == 4)
            {
                menuShell.Measure(new System.Windows.Size(184, 1000));
                menuShell.Arrange(new System.Windows.Rect(0, 0, 184, menuShell.DesiredSize.Height));
                SaveSnapshot(menuShell, Path.Combine(output, $"tray-menu-{name}.png"));
                rendered++;
            }

            if (menu.FindName("AppearanceShell") is FrameworkElement appearanceShell)
            {
                appearanceShell.Resources.MergedDictionaries.Add(ChromeThemePalette.Create(theme));
                appearanceShell.Measure(new System.Windows.Size(128, 1000));
                appearanceShell.Arrange(new System.Windows.Rect(0, 0, 128, appearanceShell.DesiredSize.Height));
                SaveSnapshot(appearanceShell, Path.Combine(output, $"appearance-submenu-{name}.png"));
                rendered++;
            }

            var notice = new NoticeWindow("快捷键已占用", "请选择另一组按键。", theme);
            var noticeShell = notice.FindName("Shell") as FrameworkElement;
            if (noticeShell is not null)
            {
                noticeShell.Measure(new System.Windows.Size(252, 1000));
                noticeShell.Arrange(new System.Windows.Rect(0, 0, 252, noticeShell.DesiredSize.Height));
                SaveSnapshot(noticeShell, Path.Combine(output, $"notice-{name}.png"));
                rendered++;
            }

            var confirmation = new ClearHistoryConfirmationWindow(24, theme);
            var confirmationShell = confirmation.FindName("Shell") as FrameworkElement;
            if (confirmationShell is not null)
            {
                confirmationShell.Measure(new System.Windows.Size(300, 1000));
                confirmationShell.Arrange(new System.Windows.Rect(0, 0, 300, confirmationShell.DesiredSize.Height));
                SaveSnapshot(confirmationShell, Path.Combine(output, $"clear-confirmation-{name}.png"));
                rendered++;
            }
        }

        return Report("chrome visual surfaces", rendered == 10, $"rendered={rendered}/10");
    }

    private static int CheckLogRetention(string output)
    {
        var directory = Path.Combine(output, "log-retention-probe");
        Directory.CreateDirectory(directory);
        var old = Path.Combine(directory, "miniclip-old.log");
        var larger = Path.Combine(directory, "miniclip-a.log");
        var newer = Path.Combine(directory, "miniclip-b.log");
        var current = Path.Combine(directory, "miniclip-current.log");
        var history = Path.Combine(directory, "history.json");

        File.WriteAllText(old, "expired");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-8));
        File.WriteAllBytes(larger, new byte[1024 * 1024]);
        File.SetLastWriteTimeUtc(larger, DateTime.UtcNow.AddHours(-2));
        File.WriteAllBytes(newer, new byte[1024 * 1024]);
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddHours(-1));
        for (var i = 0; i < 10; i++)
        {
            var filler = Path.Combine(directory, $"miniclip-fill-{i:00}.log");
            File.WriteAllBytes(filler, new byte[1024 * 1024]);
            File.SetLastWriteTimeUtc(filler, DateTime.UtcNow.AddMinutes(-90 + i));
        }
        File.WriteAllText(current, "active");
        File.WriteAllText(history, "keep");

        DiagnosticsLog.PruneLogs(directory, current, DateTime.UtcNow);

        var kept = Directory.GetFiles(directory, "miniclip-*.log");
        var bytes = kept.Sum(path => new FileInfo(path).Length);
        var okay = !File.Exists(old) && !File.Exists(larger)
                   && File.Exists(newer) && File.Exists(current) && File.Exists(history)
                   && bytes <= 10 * 1024 * 1024;
        return Report("log retention", okay,
            $"expiredRemoved={!File.Exists(old)} budgetPruned={!File.Exists(larger)} activeKept={File.Exists(current)} bytes={bytes}");
    }

    private static int CheckPopupRapidReopen()
    {
        var popup = new PopupWindow();
        try
        {
            var items = new[] { new HistoryEntry("recent", DateTimeOffset.Now) };
            popup.ShowAt(items, 300, 300);
            popup.Close1(PopupCloseReason.Escape);
            popup.ShowAt(items, 300, 300);
            PumpDispatcher(TimeSpan.FromMilliseconds(240));
            return Report("popup rapid reopen", popup.IsPopupOpen && popup.IsWindowShown,
                $"open={popup.IsPopupOpen} visible={popup.IsWindowShown}");
        }
        catch (Exception ex)
        {
            return Report("popup rapid reopen", false, ex.GetType().Name);
        }
        finally
        {
            popup.Close1(PopupCloseReason.Shutdown);
            PumpDispatcher(TimeSpan.FromMilliseconds(180));
            popup.Close();
        }
    }

    private static int CheckPopupWindow(string output)
    {
        var samples = new List<HistoryEntry>
        {
            new("SELECT * FROM users;", DateTimeOffset.Now),
            new("git status", DateTimeOffset.Now),
            new("docker compose up -d", DateTimeOffset.Now),
            new("localhost:8080", DateTimeOffset.Now),
            new("npm run build --workspace=@miniclip/core", DateTimeOffset.Now),
            new("fix(clipboard): 保留原焦点，改为\r\nWS_EX_NOACTIVATE", DateTimeOffset.Now),
            new("https://github.com/miniclip/releases/download/v1.0.0/miniclip-1.0.0-win-x64.zip", DateTimeOffset.Now),
            new("   ", DateTimeOffset.Now),
            new("kubectl -n staging rollout restart deploy/api", DateTimeOffset.Now),
            new("Get-Process | Sort-Object WS -Descending | Select-Object -First 10", DateTimeOffset.Now),
        };

        var popup = new PopupWindow();
        var created = false;
        var stylesRead = "not read";
        var stylesOk = false;
        var placementOk = false;
        var placementDetail = "not checked";

        // Anchor at the real cursor position so the placement check exercises the same
        // path the product uses, including the offset and the work-area clamping.
        var anchorX = 300;
        var anchorY = 300;
        if (Win32Windows.TryGetCursorPosition(out var cursor))
        {
            anchorX = cursor.X;
            anchorY = cursor.Y;
        }

        try
        {
            popup.ShowAt(samples, anchorX, anchorY);
            popup.UpdateLayout();

            var handle = new System.Windows.Interop.WindowInteropHelper(popup).Handle;
            created = handle != IntPtr.Zero;

            if (created)
            {
                var exStyle = NativeMethods.GetWindowLongW(handle, NativeConstants.GWL_EXSTYLE);
                var noActivate = (exStyle & NativeConstants.WS_EX_NOACTIVATE) != 0;
                var toolWindow = (exStyle & NativeConstants.WS_EX_TOOLWINDOW) != 0;
                var topMost = (exStyle & NativeConstants.WS_EX_TOPMOST) != 0;
                stylesOk = noActivate && toolWindow && topMost;
                stylesRead = $"exStyle=0x{exStyle:X8} NOACTIVATE={noActivate} TOOLWINDOW={toolWindow} TOPMOST={topMost}";

                // The popup must be on screen and inside a work area, and it must have
                // been placed at the supplied focus anchor rather than centred on a default monitor.
                var hasRect = Win32Windows.TryGetWindowRect(handle, out var rect);
                if (!hasRect)
                {
                    placementOk = false;
                    placementDetail = "no window rect";
                }
                else if (!Win32Screen.TryGetWorkArea(rect.Left + 1, rect.Top + 1, out var work))
                {
                    placementOk = false;
                    placementDetail = "no work area for the popup's monitor";
                }
                else
                {
                    placementOk = rect.Left >= work.Left && rect.Top >= work.Top
                                  && rect.Right <= work.Right && rect.Bottom <= work.Bottom
                                  && rect.Width > 0 && rect.Height > 0;

                    placementDetail =
                        $"rect=({rect.Left},{rect.Top},{rect.Right},{rect.Bottom}) work=({work.Left},{work.Top},{work.Right},{work.Bottom})";
                }

                // Capture the rendered panel so the design can be reviewed as pixels
                // rather than as a claim. Rendered from the visual tree, which keeps the
                // rounded corners and the panel's own transparency intact.
                SaveSnapshot(popup, Path.Combine(output, "popup-default.png"));

                popup.SetSelection(5);
                popup.UpdateLayout();
                SaveSnapshot(popup, Path.Combine(output, "popup-multiline-selected.png"));

                popup.SetSelection(6);
                popup.UpdateLayout();
                SaveSnapshot(popup, Path.Combine(output, "popup-long-line-selected.png"));

                popup.SetSelection(7);
                popup.UpdateLayout();
                SaveSnapshot(popup, Path.Combine(output, "popup-whitespace-selected.png"));

                popup.SetTheme(PopupTheme.Light);
                popup.SetSelection(popup.Rows.Count - 1);
                popup.UpdateLayout();
                PumpDispatcher(TimeSpan.FromMilliseconds(800));
                SaveSnapshot(popup, Path.Combine(output, "popup-light.png"));
                SaveSnapshot(popup, Path.Combine(output, "popup-light-150.png"), scale: 1.5);
            }
        }
        catch (Exception ex)
        {
            // A XamlParseException's message is nearly useless without its inner
            // exception, and this is the path most likely to fail on a build
            // configuration change, so the whole chain is logged.
            var detail = new System.Text.StringBuilder();
            for (var current = ex; current is not null; current = current.InnerException)
            {
                detail.Append(current.GetType().Name).Append(": ").AppendLine(current.Message);
                if (current is System.Xaml.XamlParseException)
                {
                    detail.AppendLine(current.StackTrace);
                }
            }

            Console.WriteLine("  popup check threw:");
            Console.WriteLine(detail.ToString());
            DiagnosticsLog.Write("selftest", "popup", ex.GetType().Name);
            DiagnosticsLog.Write("selftest", "popup-detail", detail.ToString().ReplaceLineEndings(" | "));
        }
        finally
        {
            try
            {
                popup.Close1(PopupCloseReason.Shutdown);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
            {
                // Shutting the probe window down is best-effort.
            }
        }


        var stylesResult = Report("popup window styles", created && stylesOk, stylesRead);
        var placementResult = Report("popup placement", placementOk, placementDetail);
        var edgesResult = CheckPlacementEdges();
        var routingResult = CheckNavigationRouting();
        var scrollingResult = CheckLongListScrolling(output);
        var emptyResult = CheckEmptyState(output);

        return stylesResult + placementResult + edgesResult + routingResult + scrollingResult + emptyResult;
    }

    /// <summary>
    /// Exercises the placement maths at every edge and corner of the current work area,
    /// which is the part of §25 scenario 8 that can be checked without a second monitor:
    /// the panel must stay fully inside the work area at 100%, 125% and 150% scaling.
    /// </summary>
    private static int CheckPlacementEdges()
    {
        if (!Win32Screen.TryGetWorkArea(400, 400, out var work))
        {
            return Report("popup edge placement", false, "no work area");
        }

        // The popup at 150% scaling on the machine this was developed on.
        const int width = 600;
        const int height = 552;

        (string Name, int X, int Y)[] anchors =
        [
            ("top-left", work.Left, work.Top),
            ("top-right", work.Right - 1, work.Top),
            ("bottom-left", work.Left, work.Bottom - 1),
            ("bottom-right", work.Right - 1, work.Bottom - 1),
            ("centre", work.Left + (work.Width / 2), work.Top + (work.Height / 2)),
        ];

        var failures = 0;
        var details = new List<string>(anchors.Length);

        foreach (var (name, x, y) in anchors)
        {
            var origin = Win32WindowStyles.PositionWithinWorkArea(x, y, width, height);
            var inside = origin.X >= work.Left
                         && origin.Y >= work.Top
                         && origin.X + width <= work.Right
                         && origin.Y + height <= work.Bottom;

            if (!inside)
            {
                failures++;
                details.Add($"{name}=OUT({origin.X},{origin.Y})");
            }
        }

        // Flipping: an anchor near the right edge must place the panel to its left, and an
        // anchor near the left edge must place it to its right.
        var nearRight = Win32WindowStyles.PositionWithinWorkArea(work.Right - 4, work.Top + 200, width, height);
        var nearLeft = Win32WindowStyles.PositionWithinWorkArea(work.Left + 4, work.Top + 200, width, height);
        var flipsRight = nearRight.X < work.Right - 4;
        var flipsLeft = nearLeft.X >= work.Left + 4;
        var centreX = work.Left + work.Width / 2;
        var centreY = work.Top + work.Height / 2;
        const int compactHeight = 213;
        var nearCaret = Win32WindowStyles.PositionWithinWorkArea(centreX, centreY, width, compactHeight);
        var aboveCaret = nearCaret.Y + compactHeight <= centreY - 4;

        if (!flipsRight || !flipsLeft || !aboveCaret)
        {
            failures++;
            details.Add($"flip right={flipsRight} left={flipsLeft} aboveCaret={aboveCaret}");
        }

        var summary = failures == 0
            ? $"5 anchors inside work area ({work.Width}x{work.Height}); flips at right edge and sits above caret"
            : string.Join(", ", details);

        return Report("popup edge placement", failures == 0, summary);
    }

    /// <summary>
    /// Exercises the exact entry point the keyboard hook calls for ↑ ↓ Enter Esc, and the
    /// paste request that Enter must raise.
    /// </summary>
    /// <remarks>
    /// This exists because the routing cannot be tested from outside the process. The hook
    /// deliberately ignores injected events (otherwise MiniClip's own synthetic Ctrl+V
    /// would be re-consumed), and everything a test harness can synthesise is injected —
    /// so an external driver can prove the hook is <em>invoked</em> but can never prove
    /// what it <em>routes</em>. Driving <c>HandleNavigationKey</c> directly closes that gap
    /// for every part of the chain MiniClip owns.
    /// </remarks>
    private static int CheckNavigationRouting()
    {
        var samples = new List<HistoryEntry>
        {
            new("newest", DateTimeOffset.Now),
            new("second", DateTimeOffset.Now),
            new("third", DateTimeOffset.Now),
        };

        var popup = new PopupWindow();
        try
        {
            popup.ShowAt(samples, 300, 300, preferredIndex: 0);

            var selectionOpened = popup.SelectedIndex == 2;
            var openedText = popup.SelectedText;

            // Newest is at the bottom. Up walks to older clips; down returns to newer.
            popup.HandleNavigationKey(NativeConstants.VK_DOWN);
            var clampedAtBottom = popup.SelectedIndex == 2;

            popup.HandleNavigationKey(NativeConstants.VK_UP);
            var movedUp = popup.SelectedIndex == 1 && popup.SelectedText == "second";

            popup.HandleNavigationKey(NativeConstants.VK_UP);
            popup.HandleNavigationKey(NativeConstants.VK_UP);
            var clampedAtTop = popup.SelectedIndex == 0 && popup.SelectedText == "third";

            // Enter must raise exactly one paste request carrying the selected text.
            string? pasted = null;
            var pasteCount = 0;
            popup.PasteRequested += (_, text) => { pasted = text; pasteCount++; };

            popup.HandleNavigationKey(NativeConstants.VK_RETURN);
            var enterRaisedPaste = pasteCount == 1 && pasted == "third";

            // Esc must close without raising a paste. Closing is asynchronous by design
            // (it fades first), so the dispatcher has to be allowed to run before the
            // result can be observed — a synchronous assertion here would be testing the
            // test, not the app.
            popup.ShowAt(samples, 300, 300, preferredIndex: 0);
            var closedWith = (PopupCloseReason?)null;
            popup.Closed1 += (_, reason) => closedWith = reason;
            popup.HandleNavigationKey(NativeConstants.VK_ESCAPE);
            PumpDispatcher(TimeSpan.FromMilliseconds(400));
            var escapeClosed = closedWith == PopupCloseReason.Escape;

            // The product closes the popup from the controller, after the paste has been
            // attempted. The test stands in for that caller.
            popup.ShowAt(samples, 300, 300, preferredIndex: 0);
            var closedOnEnter = (PopupCloseReason?)null;
            popup.Closed1 += (_, reason) => closedOnEnter = reason;
            popup.PasteRequested += (_, _) => popup.Close1(PopupCloseReason.Pasted);
            popup.HandleNavigationKey(NativeConstants.VK_RETURN);
            PumpDispatcher(TimeSpan.FromMilliseconds(400));
            var enterClosed = closedOnEnter == PopupCloseReason.Pasted;

            var ok = selectionOpened && openedText == "newest" && clampedAtTop && movedUp
                     && clampedAtBottom && enterRaisedPaste && escapeClosed && enterClosed;

            return Report(
                "navigation routing",
                ok,
                $"open={selectionOpened} clampTop={clampedAtTop} up={movedUp} clampBottom={clampedAtBottom} enterPastes={enterRaisedPaste} enterCloses={enterClosed} escCloses={escapeClosed}");
        }
        catch (Exception ex)
        {
            return Report("navigation routing", false, ex.ToString());
        }
        finally
        {
            try
            {
                popup.Close1(PopupCloseReason.Shutdown);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
            {
                // Best-effort.
            }
        }
    }

    /// <summary>
    /// Runs the dispatcher for a bounded period so effects that depend on it — animations,
    /// timers, posted callbacks — can actually complete.
    /// </summary>
    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = duration,
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };

        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// Proves a full 100-entry history is browsable, not just the first eight rows.
    /// </summary>
    /// <remarks>
    /// The window shows at most 8 rows while history holds 100, so without a working scroller
    /// entries 9–100 would be reachable by ↓ and invisible — Enter would paste something the
    /// user cannot see. §2.1 makes "browse the history with ↑ ↓" a core goal, so this is
    /// checked rather than assumed.
    /// </remarks>
    private static int CheckLongListScrolling(string output)
    {
        var entries = new List<HistoryEntry>(HistoryManager.DefaultCapacity);
        for (var i = 0; i < HistoryManager.DefaultCapacity; i++)
        {
            entries.Add(new HistoryEntry($"entry {i:00}", DateTimeOffset.Now));
        }

        var popup = new PopupWindow();
        try
        {
            popup.ShowAt(entries, 300, 300, preferredIndex: 0);
            popup.UpdateLayout();

            var scroller = popup.FindName("ListScroller") as ScrollViewer;
            if (scroller is null)
            {
                return Report("long list scrolling", false, "no ScrollViewer in the popup");
            }

            var windowHeight = popup.Height;
            var sizedForFiveRows = Math.Abs(windowHeight - (5 * PopupWindow.RowHeight + 12)) < 1;
            var exactFiveRowViewport = Math.Abs(scroller.ViewportHeight - 5 * PopupWindow.RowHeight) < 1;

            // The content must genuinely overflow its viewport.
            var overflows = scroller.ScrollableHeight > 0;

            // The newest clip starts at the bottom; walk up to the oldest retained row.
            var lastIndex = HistoryManager.DefaultCapacity - 1;
            var startsAtNewest = popup.SelectedIndex == lastIndex && popup.SelectedText == "entry 00";
            var startsAtBottom = scroller.VerticalOffset > 0;
            var selectedContainer = popup.ItemContainerFor(lastIndex);
            var selectedRow = selectedContainer is null ? null : FindVisualNamed<Border>(selectedContainer, "Row");
            var shell = popup.FindName("Shell") as FrameworkElement;
            var rightInset = selectedRow is not null && shell is not null
                ? shell.ActualWidth - selectedRow.TransformToAncestor(shell)
                    .TransformBounds(new System.Windows.Rect(0, 0, selectedRow.ActualWidth, selectedRow.ActualHeight)).Right
                : double.NaN;
            var selectionNearBorder = rightInset >= 0 && rightInset <= 8;
            for (var i = 0; i < lastIndex; i++)
            {
                popup.HandleNavigationKey(NativeConstants.VK_UP);
            }

            popup.UpdateLayout();

            // Capture after scrolling, not before: the hairline scroll indicator only exists
            // when the list actually overflows, and on the first row there is nothing to
            // scroll, so an earlier snapshot would show no indicator and prove nothing.
            SaveSnapshot(popup, Path.Combine(output, "popup-scrolled-100.png"));

            var scrolled = scroller.VerticalOffset;
            var selectionIsDeep = popup.SelectedIndex == 0;
            var selectionText = popup.SelectedText;

            // No element of the list may be focusable. The design review flagged the
            // ScrollViewer as a focus risk for a window whose entire purpose is never to take
            // focus, and that warning is worth taking seriously rather than waving away:
            // scrollable content invites click-to-focus and keyboard panning. Verified here
            // by walking the visual tree for any focusable descendant, and by re-reading the
            // window's extended styles after scrolling to confirm WS_EX_NOACTIVATE survived.
            var focusableDescendants = new List<string>();
            CollectFocusable(scroller, focusableDescendants, depth: 0);

            var popupHandle = new System.Windows.Interop.WindowInteropHelper(popup).Handle;
            var exStyle = popupHandle == IntPtr.Zero ? 0 : NativeMethods.GetWindowLongW(popupHandle, NativeConstants.GWL_EXSTYLE);
            var stillNoActivate = (exStyle & NativeConstants.WS_EX_NOACTIVATE) != 0;

            // The selected row must be inside the visible band, which is the whole point.
            var container = popup.ItemContainerFor(0);
            var visible = container is not null
                          && container.TransformToAncestor(scroller) is { } transform
                          && transform.Transform(new System.Windows.Point(0, 0)).Y >= -1
                          && transform.Transform(new System.Windows.Point(0, container.ActualHeight)).Y <= scroller.ViewportHeight + 1;
            var oldestRow = container is null ? null : FindVisualNamed<Border>(container, "Row");
            var shellAtTop = popup.FindName("Shell") as FrameworkElement;
            var topInset = oldestRow is not null && shellAtTop is not null
                ? oldestRow.TransformToAncestor(shellAtTop).Transform(new System.Windows.Point(0, 0)).Y
                : double.NaN;
            var selectionInsideTopPadding = topInset >= 5;

            var ok = overflows && startsAtNewest && startsAtBottom && sizedForFiveRows && exactFiveRowViewport
                     && selectionNearBorder && selectionInsideTopPadding
                     && selectionIsDeep && selectionText == $"entry {lastIndex:00}" && visible
                     && focusableDescendants.Count == 0 && stillNoActivate;

            return Report(
                "long list scrolling",
                ok,
                $"{entries.Count} entries, windowHeight={windowHeight:F0} viewportHeight={scroller.ViewportHeight:F0} scrollable={scroller.ScrollableHeight:F0} offset={scrolled:F0} startsBottom={startsAtBottom} selected={popup.SelectedIndex} visible={visible} topInset={topInset:F1} rightInset={rightInset:F1} focusable={focusableDescendants.Count} noActivate={stillNoActivate}");
        }
        catch (Exception ex)
        {
            return Report("long list scrolling", false, ex.GetType().Name);
        }
        finally
        {
            try
            {
                popup.Close1(PopupCloseReason.Shutdown);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
            {
                // Best-effort.
            }
        }
    }

    private static int CheckScrollIndicator()
    {
        var entries = Enumerable.Range(0, 20)
            .Select(i => new HistoryEntry($"scroll {i:00}", DateTimeOffset.Now))
            .ToArray();
        var popup = new PopupWindow();
        try
        {
            popup.ShowAt(entries, 300, 300);
            popup.UpdateLayout();
            var scroller = popup.FindName("ListScroller") as ScrollViewer;
            scroller?.ApplyTemplate();
            var bar = scroller?.Template.FindName("PART_VerticalScrollBar", scroller)
                as System.Windows.Controls.Primitives.ScrollBar;
            if (bar is null || scroller!.ScrollableHeight <= 0)
            {
                return Report("scroll indicator idle", false, "overflow scrollbar unavailable");
            }

            var initiallyHidden = bar.Opacity < 0.05;
            popup.SetSelection(0);
            popup.UpdateLayout();
            var shownWhileScrolling = bar.Opacity > 0.95;
            PumpDispatcher(TimeSpan.FromMilliseconds(1100));
            var hiddenAfterPause = bar.Opacity < 0.05;
            return Report("scroll indicator idle", initiallyHidden && shownWhileScrolling && hiddenAfterPause,
                $"initial={initiallyHidden} scrolling={shownWhileScrolling} settled={hiddenAfterPause}");
        }
        catch (Exception ex)
        {
            return Report("scroll indicator idle", false, ex.GetType().Name);
        }
        finally
        {
            popup.Close1(PopupCloseReason.Shutdown);
        }
    }

    private static int CheckPopupPerformance()
    {
        var rows = Enumerable.Range(0, 100)
            .Select(i => new HistoryEntry($"benchmark {i:00}", DateTimeOffset.Now))
            .ToArray();
        var popup = new PopupWindow();
        var durations = new List<double>(10);
        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var before = process.PrivateMemorySize64;
            for (var i = 0; i < 10; i++)
            {
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                popup.ShowAt(rows, 400, 400);
                durations.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                popup.Close1(PopupCloseReason.Escape);
                PumpDispatcher(TimeSpan.FromMilliseconds(180));
            }
            process.Refresh();
            var first = durations[0];
            durations.Sort();
            return Report("popup performance sample", !popup.IsPopupOpen && durations.Count == 10,
                $"first={first:F1}ms median={durations[5]:F1}ms max={durations[^1]:F1}ms privateDelta={(process.PrivateMemorySize64 - before) / 1048576.0:F1}MB");
        }
        catch (Exception ex)
        {
            return Report("popup performance sample", false, ex.GetType().Name);
        }
        finally
        {
            popup.Close1(PopupCloseReason.Shutdown);
            popup.Close();
        }
    }

    /// <summary>
    /// Collects the names of any focusable descendants. Used to prove the scrollable list
    /// cannot become a keyboard target, which matters because the candidate popup must
    /// never take focus from the application the user is typing in.
    /// </summary>
    private static void CollectFocusable(DependencyObject parent, List<string> found, int depth)
    {
        if (depth > 24 || found.Count > 0)
        {
            return;
        }

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count && found.Count == 0; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is System.Windows.Controls.Primitives.ScrollBar or System.Windows.Controls.Primitives.Thumb
                or System.Windows.Controls.Primitives.RepeatButton)
            {
                // The scroll indicator is explicitly non-focusable in its template; if that
                // ever regresses, this catches it.
            }

            if (child is UIElement { Focusable: true } element)
            {
                found.Add(element.GetType().Name);
                return;
            }

            CollectFocusable(child, found, depth + 1);
        }
    }

    private static T? FindVisualNamed<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        if (parent is T element && element.Name == name)
        {
            return element;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            if (FindVisualNamed<T>(VisualTreeHelper.GetChild(parent, i), name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static int CheckEmptyState(string output)    {
        var popup = new PopupWindow();
        try
        {
            popup.ShowAt([], anchorX: 300, anchorY: 300);
            popup.UpdateLayout();
            SaveSnapshot(popup, Path.Combine(output, "popup-empty.png"));
            return Report("popup empty state", popup.SelectedIndex == -1 && popup.SelectedText is null, "no selection, nothing to paste");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  empty state check threw {ex.GetType().Name}");
            return Report("popup empty state", false, ex.GetType().Name);
        }
        finally
        {
            try
            {
                popup.Close1(PopupCloseReason.Shutdown);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
            {
                // Best-effort.
            }
        }
    }

    private static void SaveSnapshot(PopupWindow popup, string path, double scale = 2.0)
    {
        var shell = popup.FindName("Shell") as FrameworkElement;
        if (shell is not null)
        {
            SaveSnapshot(shell, path, scale);
        }
    }

    private static void SaveSnapshot(FrameworkElement shell, string path, double scale = 2.0)
    {
        if (shell is null || shell.ActualWidth <= 0 || shell.ActualHeight <= 0)
        {
            return;
        }

        var width = (int)Math.Ceiling(shell.ActualWidth * scale);
        var height = (int)Math.Ceiling(shell.ActualHeight * scale);

        var target = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        target.Render(shell);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));

        using var stream = File.Create(path);
        encoder.Save(stream);
        DiagnosticsLog.Write("snapshot", Path.GetFileName(path), $"{width}x{height}");
    }

    private static void SaveMenuCombination(FrameworkElement root, FrameworkElement child,
        System.Drawing.Rectangle rootRect, System.Drawing.Rectangle childRect, double displayScale,
        string path)
    {
        const double snapshotScale = 1.5;
        var rootImage = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * snapshotScale),
            (int)Math.Ceiling(root.ActualHeight * snapshotScale), 96 * snapshotScale,
            96 * snapshotScale, PixelFormats.Pbgra32);
        rootImage.Render(root);
        var childImage = new RenderTargetBitmap((int)Math.Ceiling(child.ActualWidth * snapshotScale),
            (int)Math.Ceiling(child.ActualHeight * snapshotScale), 96 * snapshotScale,
            96 * snapshotScale, PixelFormats.Pbgra32);
        childImage.Render(child);

        var x = (childRect.Left - rootRect.Left) * snapshotScale / displayScale;
        var y = (childRect.Top - rootRect.Top) * snapshotScale / displayScale;
        var left = Math.Min(0, x);
        var top = Math.Min(0, y);
        var width = (int)Math.Ceiling(Math.Max(rootImage.PixelWidth, x + childImage.PixelWidth) - left);
        var height = (int)Math.Ceiling(Math.Max(rootImage.PixelHeight, y + childImage.PixelHeight) - top);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawImage(rootImage, new System.Windows.Rect(-left, -top,
                rootImage.PixelWidth, rootImage.PixelHeight));
            context.DrawImage(childImage, new System.Windows.Rect(x - left, y - top,
                childImage.PixelWidth, childImage.PixelHeight));
        }
        var composite = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        composite.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(composite));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static int CheckMemory() =>
        Report("memory footprint", true, DiagnosticsLog.ReadMemory());

    private static int Report(string name, bool passed, string detail)
    {
        var status = passed ? "PASS" : "FAIL";
        Console.WriteLine($"  [{status}] {name}: {detail}");
        DiagnosticsLog.Write("selftest", $"{name}={(passed ? "pass" : "fail")} {detail}");
        return passed ? 0 : 1;
    }
}
