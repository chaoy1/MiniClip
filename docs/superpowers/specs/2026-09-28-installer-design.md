# MiniClip installer design

## Goal

Ship a Simplified Chinese Windows installer for MiniClip 1.0.0. The user can choose the installation directory and whether MiniClip starts with Windows. The package includes the .NET 10 desktop runtime so the target computer does not need a separate runtime installation.

## Package and wizard

- Publish `win-x64`, self-contained, not single-file. Install all published files together.
- Use Inno Setup with a stable AppId, current-user installation, a visible destination page, and a Start Menu shortcut. Do not request elevation or add a desktop shortcut by default.
- Use the existing icon and a restrained modern wizard that follows Windows light/dark appearance.
- Add an optional "start with Windows" task. Fresh installations default off; upgrades reflect the current `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MiniClip` value. The installer and application settings must control the same value, pointing to the chosen installation path with quotes.
- Offer "launch MiniClip" on the finish page. Refuse installation or uninstallation while any MiniClip instance holds the existing single-instance mutex, so the user can exit it cleanly from the tray.

## Uninstall

Uninstall removes all MiniClip-owned artifacts: packaged program files, installer shortcuts and registration, the `MiniClip` startup value and its StartupApproved state, and the entire `%LOCALAPPDATA%\MiniClip` directory (history, settings, logs, and damaged-file backups). It does not recursively delete the user-selected installation directory because it may contain unrelated files. Inno removes installed files and removes the directory when empty. Windows-managed caches such as Prefetch or Recent are outside the application's ownership.

The user's request explicitly authorizes deleting MiniClip history and settings at uninstall. The installer must not delete the current user's data during build or verification.

## Build and verification

- Add a reproducible PowerShell build script and Inno Setup source. Output `dist/MiniClip-Setup-1.0.0-x64.exe`.
- Verify Release publish, native self-test, Inno compilation, source registration/cleanup paths, and a package smoke test without uninstalling the developer's live MiniClip profile.
- If no disposable Windows profile is available, report that full uninstall execution was not tested on a clean profile. Do not claim it was.
