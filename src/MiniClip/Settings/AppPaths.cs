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
        }
    }

    /// <summary>
    /// Resolves a data directory for an arbitrary application directory, without touching
    /// the cached answer the running app uses.
    /// </summary>
    /// <remarks>
    /// This exists so the portable/fallback decision can be tested for real rather than
    /// argued about. The self-test builds a throwaway folder, puts a marker in it, and asks
    /// this method — which is the same code path <see cref="Resolve"/> uses, not a copy of
    /// it, so the test cannot drift away from the behaviour it claims to check.
    /// </remarks>
    internal static Resolution ResolveFor(string applicationDirectory)
    {
        var marker = Path.Combine(applicationDirectory, PortableMarkerName);

        if (!File.Exists(marker))
        {
            return new Resolution(LegacyDataDirectory, DataLocationMode.LocalAppData, null);
        }

        var portableDirectory = Path.Combine(applicationDirectory, DataFolderName);
        return TryPrepareWritableDirectory(portableDirectory, out var failure)
            ? new Resolution(portableDirectory, DataLocationMode.Portable, null)
            : new Resolution(LegacyDataDirectory, DataLocationMode.LocalAppDataFellBack, failure);
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
    /// <summary>Data sits in <c>data\</c> next to the executable, as the installer sets up.</summary>
    Portable,

    /// <summary>No marker file; data in <c>%LOCALAPPDATA%\MiniClip</c>.</summary>
    LocalAppData,

    /// <summary>Portable was requested but the install folder is not writable; data went to AppData.</summary>
    LocalAppDataFellBack,
}

internal readonly record struct Resolution(string Directory, DataLocationMode Mode, string? FallbackReason);
