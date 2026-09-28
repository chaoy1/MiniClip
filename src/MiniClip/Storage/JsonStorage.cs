// -----------------------------------------------------------------------------
// MiniClip — clipboard history persistence.
//
// §12: the history lives in %LOCALAPPDATA%\MiniClip\history.json as an indented JSON
// array of strings, newest entry first. Every write goes through a temporary file that
// is flushed to disk and then swapped in, so an abnormal exit can never leave half a
// JSON document behind; a file that cannot be parsed is preserved for inspection
// instead of being silently overwritten.
//
// §10: entry text is stored exactly as it came off the clipboard — never trimmed, never
// whitespace- or case-normalised. Entries longer than MaxEntryLength are skipped.
//
// §27: no clipboard text ever reaches a log, an exception message or telemetry. A
// StorageResult.Detail carries paths, counts and exception type names only.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;

namespace MiniClip.Storage;

/// <summary>
/// What happened during a <see cref="JsonStorage"/> operation.
/// </summary>
public enum StorageOutcome
{
    /// <summary>The history file was written.</summary>
    Saved,

    /// <summary>The history file already held the requested payload, so nothing was written.</summary>
    Skipped,

    /// <summary>The operation failed; <see cref="StorageResult.Detail"/> carries a sanitized reason.</summary>
    Failed,

    /// <summary>Entries were read from the history file.</summary>
    Loaded,

    /// <summary>The history file does not exist.</summary>
    NotFound,

    /// <summary>
    /// The history file exists but is not a valid JSON array of strings. Its bytes were preserved
    /// under a timestamped name before the caller was told, and were not overwritten.
    /// </summary>
    Corrupt,

    /// <summary>The history file parsed, but held no usable entries.</summary>
    Empty,
}

/// <summary>
/// Result of a <see cref="JsonStorage"/> operation.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Count">
/// Number of entries written, skipped or loaded. Zero for <see cref="StorageOutcome.NotFound"/>,
/// <see cref="StorageOutcome.Corrupt"/> and <see cref="StorageOutcome.Failed"/>.
/// </param>
/// <param name="Path">Fully qualified path of the history file the operation targeted.</param>
/// <param name="Detail">
/// Sanitized explanation meant for a log file or a tooltip. It never contains clipboard text:
/// paths, counts and exception type names only. §27.
/// </param>
public sealed record StorageResult(StorageOutcome Outcome, int Count, string? Path = null, string? Detail = null)
{
    /// <summary>
    /// Gets a value indicating whether the operation finished without reporting a problem.
    /// <see cref="StorageOutcome.Corrupt"/> and <see cref="StorageOutcome.Failed"/> are the two
    /// unsuccessful outcomes; a missing file is not an error.
    /// </summary>
    public bool IsSuccess =>
        Outcome is StorageOutcome.Saved
            or StorageOutcome.Skipped
            or StorageOutcome.Loaded
            or StorageOutcome.NotFound
            or StorageOutcome.Empty;
}

/// <summary>
/// Persists the clipboard history as an indented JSON array of strings, newest entry first.
/// </summary>
/// <remarks>
/// <para>
/// The file is <c>%LOCALAPPDATA%\MiniClip\history.json</c> by default (§12 — local app data, never
/// a roaming directory, so the history does not travel with a roaming profile). Tests and tools
/// can point an instance elsewhere through the constructor.
/// </para>
/// <para>
/// Every public member is safe to call from any thread, and from several threads at once: all work
/// is serialized through an asynchronous gate, so two writes can never interleave, and no thread is
/// blocked while waiting (no <see langword="lock"/> is ever held across an <see langword="await"/>).
/// </para>
/// <para>
/// Writes are atomic. The payload is written to a temporary file in the destination directory,
/// flushed to the disk with <c>FlushFileBuffers</c>, and only then swapped in: with
/// <see cref="File.Replace(string, string, string, bool)"/> when the history file already exists,
/// with <see cref="File.Move(string, string, bool)"/> when it does not. Brief
/// <see cref="IOException"/> / <see cref="UnauthorizedAccessException"/> retries cover the case
/// where a virus scanner or another handle holds the file for a moment.
/// </para>
/// <para>
/// A history file that cannot be parsed is never overwritten silently: it is moved aside to
/// <c>history.corrupt-yyyyMMdd-HHmmss.json</c> — copied instead when renaming is not possible — and
/// the caller receives <see cref="StorageOutcome.Corrupt"/>. §12.
/// </para>
/// </remarks>
public sealed class JsonStorage
{
    /// <summary>
    /// Hard upper bound, in characters, for a single history entry (§10). Callers filter entries
    /// longer than this before saving; <see cref="LoadAsync"/> drops them and counts them in the
    /// report instead of truncating them.
    /// </summary>
    public const int MaxEntryLength = 100_000;

    /// <summary>
    /// Gets the default history file location: <c>%LOCALAPPDATA%\MiniClip\history.json</c>.
    /// </summary>
    public static string DefaultHistoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MiniClip",
        "history.json");

    /// <summary>
    /// Gets the fully qualified path of the history file this instance reads and writes.
    /// </summary>
    public string HistoryPath { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonStorage"/> class.
    /// </summary>
    /// <param name="historyPath">
    /// History file to use; <see langword="null"/>, an empty string or white space selects
    /// <see cref="DefaultHistoryPath"/>. A relative path is resolved against the current directory.
    /// </param>
    public JsonStorage(string? historyPath = null)
    {
        HistoryPath = Path.GetFullPath(string.IsNullOrWhiteSpace(historyPath) ? DefaultHistoryPath : historyPath);
    }

    /// <summary>
    /// Reads the history file into <paramref name="destination"/>, newest entry first.
    /// </summary>
    /// <param name="destination">
    /// List that receives the usable entries. It is cleared before the first entry is added; when
    /// the call fails with <see cref="StorageOutcome.Failed"/> it is left as the caller supplied it,
    /// so a transient read error never wipes the in-memory history.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="StorageOutcome.Loaded"/> with the number of entries read;
    /// <see cref="StorageOutcome.NotFound"/> when the file does not exist;
    /// <see cref="StorageOutcome.Empty"/> when the file parsed to zero usable entries (an empty
    /// array, or entries that were <see langword="null"/> or longer than
    /// <see cref="MaxEntryLength"/> — those are dropped, never truncated);
    /// <see cref="StorageOutcome.Corrupt"/> when the file exists but could not be parsed, in which
    /// case the original bytes are preserved under a timestamped name; or
    /// <see cref="StorageOutcome.Failed"/> when the file could not be read at all.
    /// The <see cref="StorageResult.Detail"/> never contains clip text.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public async Task<StorageResult> LoadAsync(IList<string> destination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(HistoryPath))
            {
                _lastPayload = null;
                destination.Clear();
                return new StorageResult(StorageOutcome.NotFound, 0, HistoryPath, "history file does not exist");
            }

            byte[] payload;
            try
            {
                payload = await ReadAllBytesAsync(HistoryPath, ct).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                // Lost a race with whoever removed the file between the check and the open.
                _lastPayload = null;
                destination.Clear();
                return new StorageResult(StorageOutcome.NotFound, 0, HistoryPath, "history file was removed while it was being read");
            }
            catch (DirectoryNotFoundException)
            {
                _lastPayload = null;
                destination.Clear();
                return new StorageResult(StorageOutcome.NotFound, 0, HistoryPath, "history directory does not exist");
            }

            List<string?>? parsed;
            try
            {
                parsed = ParseEntries(payload);
            }
            catch (JsonException ex)
            {
                return QuarantineCorruptHistoryFile(
                    $"invalid JSON ({ex.GetType().Name} at line {ex.LineNumber ?? -1}, byte {ex.BytePositionInLine ?? -1})",
                    destination);
            }

            if (parsed is null)
            {
                // The document was the JSON literal null, which is not the array we expect.
                return QuarantineCorruptHistoryFile("the JSON root is not an array of strings", destination);
            }

            var usable = new List<string>(parsed.Count);
            var droppedForLength = 0;
            var droppedForNull = 0;

            foreach (var entry in parsed)
            {
                if (entry is null)
                {
                    droppedForNull++;
                    continue;
                }

                if (entry.Length > MaxEntryLength)
                {
                    droppedForLength++;
                    continue;
                }

                usable.Add(entry);
            }

            destination.Clear();
            for (var i = 0; i < usable.Count; i++)
            {
                destination.Add(usable[i]);
            }

            // Remember the exact bytes that are on disk, so an immediately following save of the
            // same content costs nothing. Hand-edited formatting (or a BOM) simply means the next
            // save rewrites the file, which is the desired canonicalisation.
            _lastPayload = payload;

            var dropped = droppedForLength + droppedForNull;
            if (usable.Count == 0)
            {
                return new StorageResult(
                    StorageOutcome.Empty,
                    0,
                    HistoryPath,
                    dropped == 0
                        ? "history file holds an empty array"
                        : $"history file holds no usable entries (dropped {dropped})");
            }

            return new StorageResult(
                StorageOutcome.Loaded,
                usable.Count,
                HistoryPath,
                dropped == 0
                    ? $"loaded {usable.Count} entries"
                    : $"loaded {usable.Count} entries and dropped {dropped} ({droppedForLength} longer than {MaxEntryLength} characters, {droppedForNull} null)");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new StorageResult(StorageOutcome.Failed, 0, HistoryPath, $"load failed: {ex.GetType().Name}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Atomically writes <paramref name="entries"/> to the history file, newest entry first.
    /// </summary>
    /// <param name="entries">
    /// Entries to persist, newest first. Text is written verbatim — this method never trims,
    /// re-cases or normalises anything. Callers are responsible for filtering out entries longer
    /// than <see cref="MaxEntryLength"/> before calling; nothing is skipped or truncated here.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="StorageOutcome.Saved"/> with the number of entries written;
    /// <see cref="StorageOutcome.Skipped"/> when the serialized payload is byte-identical to the
    /// payload this instance wrote or loaded last and the file is still in place, so a repeat save
    /// costs no disk I/O; or <see cref="StorageOutcome.Failed"/> when the payload could not be
    /// written, in which case the previous history file is left untouched.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public async Task<StorageResult> SaveAsync(IReadOnlyList<string> entries, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var payload = CreatePayload(entries);

            if (IsPayloadUnchanged(payload))
            {
                return new StorageResult(StorageOutcome.Skipped, entries.Count, HistoryPath, "history file already holds this payload");
            }

            // The directory is created once, outside the retry loop: a missing directory is a
            // configuration problem, not the momentary lock two retries are meant to survive.
            var directory = ResolveHistoryDirectory();
            Directory.CreateDirectory(directory);
            var tempPath = Path.Combine(directory, BuildTempFileName());

            await ExecuteWithRetryAsync("save", () => WriteOnceAsync(tempPath, payload, ct), ct).ConfigureAwait(false);

            _lastPayload = payload;

            return new StorageResult(
                StorageOutcome.Saved,
                entries.Count,
                HistoryPath,
                entries.Count == 0 ? "saved an empty history" : $"saved {entries.Count} entries");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new StorageResult(StorageOutcome.Failed, 0, HistoryPath, $"save failed: {ex.GetType().Name}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Deletes the history file (the tray "clear history" action, §27). Only MiniClip's own record
    /// is removed; the Windows clipboard itself is left alone.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="StorageOutcome.Saved"/> when the file was removed,
    /// <see cref="StorageOutcome.NotFound"/> when it was already absent, or
    /// <see cref="StorageOutcome.Failed"/> when it could not be removed.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public async Task<StorageResult> DeleteAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(HistoryPath))
            {
                _lastPayload = null;
                return new StorageResult(StorageOutcome.NotFound, 0, HistoryPath, "history file does not exist");
            }

            await ExecuteWithRetryAsync(
                "delete",
                () =>
                {
                    File.Delete(HistoryPath);
                    return Task.CompletedTask;
                },
                ct).ConfigureAwait(false);

            // Forget the cached payload: the next save must reach the disk even when the caller
            // writes back exactly what was deleted.
            _lastPayload = null;

            return new StorageResult(StorageOutcome.Saved, 0, HistoryPath, "history file deleted");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new StorageResult(StorageOutcome.Failed, 0, HistoryPath, $"delete failed: {ex.GetType().Name}");
        }
        finally
        {
            _gate.Release();
        }
    }

    // -- tunables -------------------------------------------------------------

    private const int MaxIoAttempts = 5;
    private const int RetryBaseDelayMilliseconds = 50;
    private const int FileStreamBufferSize = 4096;
    private const long MaxPreallocatedReadBytes = 64L * 1024 * 1024;
    private const string TempFileSuffix = ".tmp";

    /// <summary>
    /// Strict UTF-8 without a byte-order mark: the payload is always encoded through this instance,
    /// so a BOM can never end up in the file. JsonSerializer already emits BOM-less UTF-8; being
    /// explicit keeps that promise visible at the write site.
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Shared serializer settings: indented so the file stays readable and hand-editable, non-ASCII
    /// text (CJK, emoji) kept literal, and hand edits tolerated on the way back in. Frozen on first
    /// use by System.Text.Json, which makes the instance safe to share.
    /// </summary>
    /// <remarks>
    /// The encoder choice is deliberate and was measured rather than assumed. The planning document
    /// specifies this file as a plain, human-readable JSON array a user can open and edit (§12), and
    /// the encoders differ in exactly that respect:
    /// <list type="bullet">
    /// <item><c>default</c> — escapes CJK (<c>\u4E2D\u6587</c>) and quotes (<c>\u0022</c>): unreadable.</item>
    /// <item><c>Create(UnicodeRanges.All)</c> — keeps CJK literal but still escapes <c>"</c> and
    /// <c>&amp;</c>, so a copied code snippet lands on disk as escape soup.</item>
    /// <item><c>UnsafeRelaxedJsonEscaping</c> — this one. Quotes and CJK stay literal.</item>
    /// </list>
    /// "Unsafe" refers only to relaxing HTML-sensitive escaping, which matters when JSON is embedded
    /// in markup. This file is never embedded in markup; it is written to the user's own profile and
    /// read back by the same program.
    /// </remarks>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// The payload this instance wrote or loaded last, used to make repeat saves free. Guarded by
    /// <see cref="_gate"/>; never read or written outside it.
    /// </summary>
    private byte[]? _lastPayload;

    // -- payloads -------------------------------------------------------------

    /// <summary>
    /// Serializes the entries to the exact bytes that belong in the file. Entry strings are handed
    /// to the serializer untouched.
    /// </summary>
    private static byte[] CreatePayload(IReadOnlyList<string> entries)
    {
        // Snapshot through List<string> so the serializer always sees the canonical collection type.
        var snapshot = entries as List<string> ?? new List<string>(entries);
        var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
        return Utf8NoBom.GetBytes(json);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the file already contains exactly these bytes, in which
    /// case the write is skipped. The existence check keeps the optimisation honest when the file
    /// was removed behind our back (by the tray action or by the user).
    /// </summary>
    private bool IsPayloadUnchanged(byte[] payload)
    {
        var previous = _lastPayload;
        return previous is not null
            && previous.Length == payload.Length
            && previous.AsSpan().SequenceEqual(payload.AsSpan())
            && File.Exists(HistoryPath);
    }

    /// <summary>
    /// Parses the file contents into a string list. A leading UTF-8 byte-order mark is tolerated —
    /// MiniClip never writes one, but a hand edit in some Windows editors adds one — and anything
    /// the reader rejects (including bytes that are not valid UTF-8) surfaces as
    /// <see cref="JsonException"/>.
    /// </summary>
    private static List<string?>? ParseEntries(byte[] payload)
    {
        ReadOnlySpan<byte> json = payload;
        if (json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF)
        {
            json = json[3..];
        }

        return JsonSerializer.Deserialize<List<string?>>(json, SerializerOptions);
    }

    // -- reading --------------------------------------------------------------

    private static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct)
    {
        // FileShare.ReadWrite | FileShare.Delete: a concurrent save (File.Replace) must not fail
        // just because this reader holds the file.
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            FileStreamBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        try
        {
            var capacity = 0;
            var length = stream.Length;
            if (length > 0 && length <= MaxPreallocatedReadBytes)
            {
                capacity = (int)length;
            }

            using var buffer = new MemoryStream(capacity);
            await stream.CopyToAsync(buffer, FileStreamBufferSize, ct).ConfigureAwait(false);
            return buffer.ToArray();
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    // -- writing --------------------------------------------------------------

    /// <summary>
    /// One write attempt: fill the temporary file, then swap it in. The temporary file never
    /// outlives the attempt — after a successful swap it is already gone, after a failure it must
    /// not be left lying around.
    /// </summary>
    private async Task WriteOnceAsync(string tempPath, byte[] payload, CancellationToken ct)
    {
        try
        {
            await WriteTempFileAsync(tempPath, payload, ct).ConfigureAwait(false);
            ReplaceDestinationFile(tempPath, HistoryPath);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static async Task WriteTempFileAsync(string tempPath, byte[] payload, CancellationToken ct)
    {
        var stream = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            FileStreamBufferSize,
            FileOptions.Asynchronous);

        try
        {
            await stream.WriteAsync(payload.AsMemory(), ct).ConfigureAwait(false);

            // FlushFileBuffers: the bytes have to be on the disk before the temp file replaces the
            // history file, otherwise a power loss could publish a truncated payload.
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Swaps the finished temporary file in for the history file. <see cref="File.Replace(string, string, string, bool)"/>
    /// keeps the swap atomic when the destination exists; a plain move covers the first save and
    /// the case where the destination vanished just before the replace.
    /// </summary>
    private static void ReplaceDestinationFile(string tempPath, string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            File.Move(tempPath, destinationPath, overwrite: true);
            return;
        }

        try
        {
            File.Replace(tempPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        catch (FileNotFoundException)
        {
            File.Move(tempPath, destinationPath, overwrite: true);
        }
    }

    /// <summary>
    /// Runs <paramref name="operation"/> and retries it briefly when the file system reports a
    /// transient problem: an anti-virus scanner or another handle can hold the history file for a
    /// few tens of milliseconds. Cancellation is honoured between attempts; every other exception
    /// propagates immediately.
    /// </summary>
    private static async Task ExecuteWithRetryAsync(string operationName, Func<Task> operation, CancellationToken ct)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxIoAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await operation().ConfigureAwait(false);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastError = ex;
            }

            if (attempt < MaxIoAttempts)
            {
                await Task.Delay(GetRetryDelay(attempt), ct).ConfigureAwait(false);
            }
        }

        throw new IOException(
            $"The {operationName} operation failed after {MaxIoAttempts} attempts (last error: {lastError?.GetType().Name ?? "none"}).",
            lastError);
    }

    /// <summary>50 ms, 100 ms, 200 ms, 400 ms — short enough to stay invisible, long enough for a scanner.</summary>
    private static TimeSpan GetRetryDelay(int attempt) =>
        TimeSpan.FromMilliseconds(RetryBaseDelayMilliseconds << (attempt - 1));

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover *.tmp file is harmless and must never turn an otherwise
            // successful save into a failure.
        }
    }

    // -- corrupt files --------------------------------------------------------

    /// <summary>
    /// Handles a file that exists but cannot be interpreted. The user's bytes are never deleted or
    /// overwritten: the file is renamed to <c>history.corrupt-yyyyMMdd-HHmmss.json</c>, or copied
    /// when renaming is blocked, and the caller gets <see cref="StorageOutcome.Corrupt"/> with an
    /// empty destination list. No clip text reaches the detail.
    /// </summary>
    private StorageResult QuarantineCorruptHistoryFile(string reason, IList<string> destination)
    {
        _lastPayload = null;
        destination.Clear();

        var quarantinePath = BuildQuarantinePath(HistoryPath, DateTime.Now);
        string detail;

        try
        {
            File.Move(HistoryPath, quarantinePath);
            detail = $"history file is not a valid JSON array of strings ({reason}); original preserved as {quarantinePath}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Copy(HistoryPath, quarantinePath, overwrite: false);
                detail = $"history file is not a valid JSON array of strings ({reason}); original copied to {quarantinePath} because it could not be renamed ({ex.GetType().Name})";
            }
            catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException)
            {
                // Nothing was moved, copied or deleted, so the original is still exactly where it
                // was; the caller is told that preserving it failed.
                detail = $"history file is not a valid JSON array of strings ({reason}); the original file was left untouched because it could not be preserved ({copyError.GetType().Name})";
                return new StorageResult(StorageOutcome.Failed, 0, HistoryPath, detail);
            }
        }

        return new StorageResult(StorageOutcome.Corrupt, 0, HistoryPath, detail);
    }

    private static string BuildQuarantinePath(string historyPath, DateTime timestamp)
    {
        var directory = ResolveDirectory(historyPath);
        var baseName = Path.GetFileNameWithoutExtension(historyPath);
        var extension = Path.GetExtension(historyPath);
        var stamp = timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        var candidate = Path.Combine(directory, $"{baseName}.corrupt-{stamp}{extension}");

        // Two corrupt files inside the same second must not overwrite each other.
        for (var suffix = 1; File.Exists(candidate); suffix++)
        {
            candidate = Path.Combine(directory, $"{baseName}.corrupt-{stamp}-{suffix}{extension}");
        }

        return candidate;
    }

    // -- paths ----------------------------------------------------------------

    private string ResolveHistoryDirectory() => ResolveDirectory(HistoryPath);

    private static string ResolveDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        return string.IsNullOrEmpty(directory) ? AppContext.BaseDirectory : directory;
    }

    private string BuildTempFileName() =>
        $"{Path.GetFileName(HistoryPath)}.{Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)}{TempFileSuffix}";
}
