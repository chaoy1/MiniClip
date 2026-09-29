using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using MiniClip.Settings;

namespace MiniClip.Diagnostics;

/// <summary>
/// A deliberately boring diagnostic log.
/// </summary>
/// <remarks>
/// Non-negotiable rule, from the product's own privacy boundary: this log never contains
/// clipboard text, in any form, not even truncated. It records counts, states and
/// outcomes only. Every call site is responsible for that, which is why the writers take
/// numbers and enumerable states rather than strings built from user content. §27.
/// </remarks>
public static class DiagnosticsLog
{
    private const long MaxFileBytes = 1024 * 1024;
    private const long MaxDirectoryBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly object Gate = new();
    private static string? _path;
    private static bool _usesDefaultDirectory;
    private static long _bytesWritten;
    private static DateTime _nextMaintenanceUtc;
    private static int _rotation;

    internal static void PruneLogs(string directory, string currentPath, DateTime utcNow)
    {
        try
        {
            var files = Directory.EnumerateFiles(directory, "miniclip-*.log", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists)
                .OrderBy(file => file.LastWriteTimeUtc)
                .ToArray();
            var cutoff = utcNow - Retention;
            long total = files.Sum(file => file.Length);
            var deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                if (file.LastWriteTimeUtc >= cutoff || SamePath(file.FullName, currentPath))
                {
                    continue;
                }

                if (TryDelete(file.FullName))
                {
                    total -= file.Length;
                    deleted.Add(file.FullName);
                }
            }

            foreach (var file in files)
            {
                if (total <= MaxDirectoryBytes)
                {
                    break;
                }

                if (deleted.Contains(file.FullName) || SamePath(file.FullName, currentPath))
                {
                    continue;
                }

                if (TryDelete(file.FullName))
                {
                    total -= file.Length;
                    deleted.Add(file.FullName);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Log maintenance must never stop clipboard capture or startup.
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>True when a log has been started.</summary>
    public static bool IsEnabled => _path is not null;

    /// <summary>
    /// Where logs go: the application directory by default, <c>%LOCALAPPDATA%\MiniClip</c>
    /// only when that directory is not writable. See <see cref="AppPaths"/>.
    /// </summary>
    public static string DefaultDirectory => AppPaths.DataDirectory;

    /// <summary>Starts writing to <paramref name="path"/>, or to the default log file.</summary>
    /// <remarks>
    /// The default log file name carries the process id. It did not always, and the reason
    /// is worth keeping: <c>File.AppendAllText</c> is not exclusive, so when two MiniClip
    /// processes logged to the same file — a resident tray instance plus a self-test, which
    /// is a normal combination — their lines interleaved and a self-test's evidence file
    /// could end mid-run with no result line, looking like a hang that never happened.
    /// A per-process file makes each run's evidence complete and self-contained.
    /// </remarks>
    public static void Start(string? path = null)
    {
        lock (Gate)
        {
            _usesDefaultDirectory = path is null;
            var now = DateTime.UtcNow;
            _path = path ?? NewDefaultPath(now);
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _bytesWritten = File.Exists(_path) ? new FileInfo(_path).Length : 0;
            _nextMaintenanceUtc = now.AddDays(1);
            if (_usesDefaultDirectory)
            {
                PruneLogs(DefaultDirectory, _path, now);
            }

            Write("session", $"pid={Environment.ProcessId}");
            Write("session", $"version={Assembly.GetEntryAssembly()?.GetName().Version}");
            Write("session", $"os={Environment.OSVersion.Version}");
            Write("session", $"dpiAwareness={ReadDpiAwareness()}");
            Write("session", $"dotnet={Environment.Version}");
            Write("session", $"log={Shorten(_path)}");
        }
    }

    public static void Write(string category, string message)
    {
        lock (Gate)
        {
            if (_path is null)
            {
                return;
            }

            try
            {
                var line = $"{DateTime.Now:HH:mm:ss.fff} {category,-10} {message}{Environment.NewLine}";
                var bytes = System.Text.Encoding.UTF8.GetByteCount(line);
                if (_usesDefaultDirectory)
                {
                    var now = DateTime.UtcNow;
                    if (_bytesWritten + bytes > MaxFileBytes || now >= _nextMaintenanceUtc)
                    {
                        _path = NewDefaultPath(now);
                        _bytesWritten = 0;
                        _nextMaintenanceUtc = now.AddDays(1);
                        PruneLogs(DefaultDirectory, _path, now);
                    }
                }

                File.AppendAllText(_path, line);
                _bytesWritten += bytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never be the reason something fails.
            }
        }
    }

    private static string NewDefaultPath(DateTime now) =>
        Path.Combine(DefaultDirectory,
            $"miniclip-{Environment.ProcessId}-{now:yyyyMMdd-HHmmssfff}-{_rotation++:D3}.log");

    public static void Write(string category, string key, object? value) =>
        Write(category, $"{key}={value}");

    /// <summary>
    /// Reports what Windows actually thinks this process's DPI awareness is, rather than
    /// what the manifest asked for. The popup's positioning depends on real per-monitor
    /// awareness, so this is worth being able to check.
    /// </summary>
    public static string ReadDpiAwareness()
    {
        try
        {
            var context = DpiNative.GetThreadDpiAwarenessContext();
            var awareness = DpiNative.GetAwarenessFromDpiAwarenessContext(context);
            return awareness switch
            {
                0 => "unaware",
                1 => "system",
                2 => "per-monitor",
                3 => "per-monitor-v2",
                _ => $"unknown({awareness})",
            };
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return "unavailable";
        }
    }

    /// <summary>Truncates a path for logging without ever touching clip content.</summary>
    public static string Shorten(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(home, StringComparison.OrdinalIgnoreCase)
            ? "%USERPROFILE%" + path[home.Length..]
            : path;
    }

    /// <summary>Best-effort process memory readout for the footprint target in §26.</summary>
    public static string ReadMemory()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            return $"workingSet={process.WorkingSet64 / (1024 * 1024)}MB private={process.PrivateMemorySize64 / (1024 * 1024)}MB";
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return "workingSet=unknown";
        }
    }
}

/// <summary>
/// P/Invoke declarations used only by diagnostics, kept out of the main interop surface
/// so the shipping code path does not carry them.
/// </summary>
internal static class DpiNative
{
    [DllImport("user32.dll")]
    internal static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    internal static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);
}
