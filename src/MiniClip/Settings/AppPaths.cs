using System.IO;

namespace MiniClip.Settings;

/// <summary>
/// Where MiniClip keeps its own files.
/// </summary>
/// <remarks>
/// <para>Two layouts are supported, and which one applies is decided once at startup:</para>
/// <list type="bullet">
/// <item><b>Portable</b> — a <c>MiniClip.portable</c> marker file sits next to the
/// executable, and the data lives in that same directory (<c>data\</c>). This is what the
/// installer sets up, so a user-chosen install folder also holds the history, settings and
/// logs, and uninstalling can genuinely remove everything.</item>
/// <item><b>Roaming-install</b> — no marker, so the data goes to
/// <c>%LOCALAPPDATA%\MiniClip</c>. This is what the project plan specifies (§12) and what
/// a build run straight from <c>bin\</c> uses.</item>
/// </list>
/// <para>The permission rule is not optional and is the reason for the writability probe:
/// a process running as a normal user cannot write into <c>C:\Program Files</c> or anywhere
/// else under a machine-wide ACL. If the install directory is not writable, silently
/// failing to save the history would be the worst possible outcome for a clipboard tool, so
/// the app falls back to <c>%LOCALAPPDATA%</c> and says so rather than pretending.</para>
/// </remarks>
public static class AppPaths
{
    /// <summary>Marker file name that selects portable mode. Created by the installer.</summary>
    public const string PortableMarkerName = "MiniClip.portable";

    /// <summary>Subdirectory of the application folder that holds the data in portable mode.</summary>
    public const string DataFolderName = "data";

    private static readonly object Gate = new();
    private static Resolution? _cached;

    /// <summary>Guards the one-time import so it cannot run twice in a session.</summary>
    private static bool _importDone;

    private static Resolution Current
    {
        get
        {
            var cached = _cached;
            if (cached is { } value)
            {
                return value;
            }

            lock (Gate)
            {
                // Resolving twice is harmless (it only reads the filesystem), but doing it
                // under the lock keeps the probe file from being created twice on startup.
                return _cached ??= Resolve();
            }
        }
    }

    /// <summary>The directory holding <c>history.json</c>, <c>settings.json</c> and the logs.</summary>
    public static string DataDirectory => Current.Directory;

    /// <summary>True when the data lives next to the executable.</summary>
    public static bool IsPortable => Current.Mode == DataLocationMode.Portable;

    /// <summary>The application directory — where <c>MiniClip.exe</c> lives.</summary>
    public static string ApplicationDirectory
    {
        get
        {
            var directory = AppContext.BaseDirectory;
            return string.IsNullOrEmpty(directory)
                ? Directory.GetCurrentDirectory()
                : directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    /// <summary>How the location was decided, for the tray notice and the self-test.</summary>
    public static DataLocationMode Mode => Current.Mode;

    /// <summary>
    /// Why portable mode was abandoned, or null when it was used or never requested.
    /// Short and user-facing; never contains clip content.
    /// </summary>
    public static string? FallbackReason => Current.FallbackReason;

    /// <summary>Recalculates the location. Used by the self-test, which relocates files.</summary>
    internal static void Invalidate()
    {
        lock (Gate)
        {
            _cached = null;
            _importDone = false;
        }
    }

    /// <summary>
    /// Prepares the data directory — creating it, and performing the one-time import of
    /// pre-existing <c>%LOCALAPPDATA%</c> data when this copy is allowed to. Idempotent.
    /// </summary>
    /// <remarks>
    /// Must run before anything reads history or settings. <see cref="DataDirectory"/> alone
    /// cannot do it, because it is read from many places, including property getters, and an
    /// import is a side effect that must happen exactly once and only at startup.
    /// </remarks>
    /// <returns>True when existing data was imported.</returns>
    public static bool EnsureDataDirectory()
    {
        lock (Gate)
        {
            if (_importDone)
            {
                return false;
            }

            _importDone = true;

            var resolution = Current;
            if (resolution.Mode != DataLocationMode.Portable || !resolution.MayImportLegacyData)
            {
                return false;
            }

            return TryImportLegacyData(resolution.Directory, out _);
        }
    }

    /// <summary>
    /// Resolves a data directory for an arbitrary application directory, without touching
    /// the cached answer the running app uses.
    /// </summary>
    /// <remarks>
    /// <para>Data beside the executable is the <b>default</b>, not an opt-in. A user who
    /// unzips the portable package expects it to be self-contained and to leave nothing
    /// behind elsewhere; that was previously only true if they knew to create a marker
    /// file, which is not a reasonable thing to require.</para>
    /// <para>The marker file's remaining job is narrower and important: it authorises
    /// <b>importing</b> data that already exists in <c>%LOCALAPPDATA%\MiniClip</c>. A build
    /// run straight from <c>bin\</c> also gets its own <c>data\</c> folder, and it must not
    /// help itself to the real installation's history. Only an installer-created marker
    /// says "this copy is allowed to adopt the existing data".</para>
    /// <para>If the application directory is not writable — an unusual place to unpack an
    /// archive, but possible — the app falls back to <c>%LOCALAPPDATA%</c> rather than
    /// failing to save anything.</para>
    /// </remarks>
    internal static Resolution ResolveFor(string applicationDirectory)
    {
        var marker = Path.Combine(applicationDirectory, PortableMarkerName);
        var mayImport = File.Exists(marker);
        var portableDirectory = Path.Combine(applicationDirectory, DataFolderName);

        if (TryPrepareWritableDirectory(portableDirectory, out var failure))
        {
            return new Resolution(portableDirectory, DataLocationMode.Portable, null, mayImport);
        }

        // Portable is the default but this folder refuses writes; keep working from the
        // user data directory and report why.
        return new Resolution(LegacyDataDirectory, DataLocationMode.LocalAppDataFellBack, failure, false);
    }

    /// <summary>
    /// Copies an existing <c>%LOCALAPPDATA%\MiniClip</c> into the portable data directory,
    /// once, when the marker authorises it and the destination has no data yet.
    /// </summary>
    /// <remarks>
    /// Without this, switching the portable package to portable data would look to an
    /// existing user like their entire clipboard history had been wiped — the files would
    /// still be on disk, but the app would no longer read them. Copying rather than moving
    /// is deliberate: if anything goes wrong afterwards, the original is still there.
    /// User-level files are copied one by one and a failure on any single file is not fatal.
    /// </remarks>
    internal static bool TryImportLegacyData(string portableDirectory, out int copied) =>
        TryImportFrom(LegacyDataDirectory, portableDirectory, out copied);

    /// <summary>
    /// The import itself, with the source injectable so it can be tested against a throwaway
    /// directory instead of the user's real <c>%LOCALAPPDATA%</c>. The self-test must never
    /// touch real data to prove a behaviour.
    /// </summary>
    internal static bool TryImportFrom(string sourceDirectory, string portableDirectory, out int copied)
    {
        copied = 0;

        if (!Directory.Exists(sourceDirectory))
        {
            return false;
        }

        if (string.Equals(
                Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(portableDirectory).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Never overwrite: if this copy already has its own history, it is not a first run.
        if (File.Exists(Path.Combine(portableDirectory, "history.json")))
        {
            return false;
        }

        string[] importable =
        [
            "history.json",
            "settings.json",
        ];

        foreach (var name in importable)
        {
            var source = Path.Combine(sourceDirectory, name);
            if (!File.Exists(source))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(portableDirectory);
                File.Copy(source, Path.Combine(portableDirectory, name), overwrite: false);
                copied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A single file that cannot be copied must not abort startup; the app simply
                // begins without it, and the original is untouched.
            }
        }

        return copied > 0;
    }

    private static Resolution Resolve() => ResolveFor(ApplicationDirectory);

    /// <summary>
    /// <c>%LOCALAPPDATA%\MiniClip</c> — deliberately local rather than roaming, so history
    /// never rides along with a roaming profile. §12.
    /// </summary>
    public static string LegacyDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MiniClip");

    /// <summary>
    /// Creates the directory if needed and proves it is writable by actually writing.
    /// </summary>
    /// <remarks>
    /// A directory can exist and still refuse writes (Program Files for a normal user), so
    /// existence is not the question. The probe is a real create/write/delete of a uniquely
    /// named zero-byte file, which is the only reliable answer and costs nothing.
    /// </remarks>
    private static bool TryPrepareWritableDirectory(string directory, out string failure)
    {
        failure = string.Empty;

        try
        {
            Directory.CreateDirectory(directory);

            var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
            using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.WriteByte(0);
            }

            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            // English on purpose: this string travels through the diagnostic log, which is
            // written to a file read by tools that do not know the console's code page. The
            // user-facing wording is added where it is displayed, not here.
            failure = $"install directory not writable ({ex.GetType().Name})";
            return false;
        }
    }
}

/// <summary>Which layout the data directory came from.</summary>
public enum DataLocationMode
{
    /// <summary>
    /// Data sits in <c>data\</c> next to the executable. This is the default for every
    /// layout, portable package included.
    /// </summary>
    Portable,

    /// <summary>
    /// The application directory is not writable, so data went to <c>%LOCALAPPDATA%\MiniClip</c>.
    /// The user is told why.
    /// </summary>
    LocalAppDataFellBack,
}

/// <summary>
/// The resolved location. <paramref name="MayImportLegacyData"/> is true only for an
/// installer-created copy, which is allowed to adopt data left in <c>%LOCALAPPDATA%</c>.
/// </summary>
internal readonly record struct Resolution(
    string Directory,
    DataLocationMode Mode,
    string? FallbackReason,
    bool MayImportLegacyData = false);
