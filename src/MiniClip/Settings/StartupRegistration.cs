using System.IO;
using Microsoft.Win32;

namespace MiniClip.Settings;

/// <summary>
/// "Start with Windows" for MiniClip, stored the way Windows expects: a value under the
/// per-user Run key, which needs no elevation and no scheduled task, and which the user
/// can see and remove in Task Manager's Startup tab.
/// </summary>
/// <remarks>
/// Chosen deliberately over a Startup-folder shortcut: a shortcut would be a second file
/// to keep in sync with wherever the executable lives, and the Run key is what Windows'
/// own Startup Apps UI reads and writes.
/// </remarks>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MiniClip";

    /// <summary>True when the Run entry exists and points at an executable on disk.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key?.GetValue(ValueName) is not string command || command.Length == 0)
            {
                return false;
            }

            var path = ExtractExecutablePath(command);
            return path.Length > 0 && File.Exists(path);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Adds or removes the Run entry. Returns false when the registry refused.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                // Fall back to the application directory. Assembly.Location is deliberately
                // NOT used here: the single-file publish (which BUILD.md documents and the
                // release path uses) embeds the assembly in the host, so Location always
                // returns an empty string and the fallback would silently do nothing —
                // "start with Windows" would just fail to register. The compiler says so:
                // warning IL3000. AppContext.BaseDirectory is the supported way to find the
                // app directory in a single-file app.
                var directory = AppContext.BaseDirectory;
                if (!string.IsNullOrEmpty(directory))
                {
                    executable = Path.Combine(directory, "MiniClip.exe");
                }
            }

            if (string.IsNullOrEmpty(executable))
            {
                return false;
            }

            // Quoted so a path with spaces survives parsing by the shell.
            key.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Reads the executable path out of a Run value, quoted or not.</summary>
    private static string ExtractExecutablePath(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : string.Empty;
        }

        var space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }
}
