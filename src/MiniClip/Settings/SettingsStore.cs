using System.IO;

namespace MiniClip.Settings;

/// <summary>
/// Loads and saves <see cref="MiniClipSettings"/> as JSON next to the history file.
/// Corrupt or unreadable settings must never stop MiniClip from starting: a bad
/// file is preserved for inspection and defaults are used.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsStore(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? Path.Combine(DefaultDirectory, "settings.json");
    }

    /// <summary>
    /// The directory settings live in: the application directory by default,
    /// <c>%LOCALAPPDATA%\MiniClip</c> only when that is not writable.
    /// See <see cref="AppPaths"/>.
    /// </summary>
    public static string DefaultDirectory => AppPaths.DataDirectory;

    public string SettingsPath { get; }

    /// <summary>Raised after a successful save.</summary>
    public event EventHandler<MiniClipSettings>? Saved;

    public MiniClipSettings Current { get; private set; } = MiniClipSettings.Default;

    /// <summary>Loads settings, falling back to defaults on any failure.</summary>
    public async Task<MiniClipSettings> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(SettingsPath))
            {
                Current = MiniClipSettings.Default;
                return Current;
            }

            await using var stream = new FileStream(
                SettingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var loaded = await JsonSerializer.DeserializeAsync<MiniClipSettings>(stream, Options, ct)
                .ConfigureAwait(false);

            Current = loaded ?? MiniClipSettings.Default;
            return Current;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            PreserveCorruptFile();
            Current = MiniClipSettings.Default;
            return Current;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Saves settings atomically. Returns false when the write failed.</summary>
    public async Task<bool> SaveAsync(MiniClipSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = SettingsPath + ".tmp";
            await using (var stream = new FileStream(
                             temp,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, settings, Options, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }

            if (File.Exists(SettingsPath))
            {
                File.Replace(temp, SettingsPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, SettingsPath, overwrite: true);
            }

            Current = settings;
            Saved?.Invoke(this, settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            TryDelete(SettingsPath + ".tmp");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void PreserveCorruptFile()
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var preserved = Path.Combine(
                Path.GetDirectoryName(SettingsPath) ?? DefaultDirectory,
                $"settings.corrupt-{stamp}.json");
            File.Copy(SettingsPath, preserved, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Preserving the file is best-effort; starting up is not.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing useful to do.
        }
    }
}
