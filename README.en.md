# MiniClip

<img src="docs/images/app-icon.png" alt="MiniClip application icon" width="96">

**Language:** [简体中文](README.md) · English

MiniClip is a text-only clipboard history app for Windows 10 and 11. Press a global shortcut to show recent entries near the text cursor, select one with the arrow keys, and press Enter to paste it. The popup does not take keyboard focus, so the original input window remains active.

[Download the latest release](https://github.com/chaoy1/MiniClip/releases/latest) · [Build and release guide (Chinese)](docs/BUILD.md) · [Verification guide (Chinese)](docs/VERIFICATION.md)

| Light | Dark |
| --- | --- |
| ![Light clipboard popup](docs/images/popup-light.png) | ![Dark clipboard popup](docs/images/popup-dark.png) |

## Installation

Choose a Windows x64 download from [GitHub Releases](https://github.com/chaoy1/MiniClip/releases/latest):

| File | Requirements |
| --- | --- |
| `MiniClip-Setup-<version>-x64.exe` | Recommended. The installer includes the .NET 10 Desktop Runtime. |
| `MiniClip-<version>-win-x64.zip` | Portable package. Extract all files and run `MiniClip.exe`. The computer must already have the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). |

The installer uses a per-user location by default. You can choose another location and optionally create a desktop shortcut or enable startup with Windows. The installer is currently unsigned, so Windows may ask you to confirm its source.

## Usage

| Key | Action |
| --- | --- |
| `Alt + V` | Open or close the popup. You can change this shortcut in Settings. |
| `↑` / `↓` | Select a history entry. |
| `Enter` | Copy the selected text to the system clipboard and paste it into the original window. |
| `Esc` | Close the popup. |

MiniClip runs in the system tray. Right-click its icon to clear history, change the appearance, open Settings, or exit. Double-click the icon to open Settings. History holds up to 100 entries, subject to a total limit of 500,000 characters. MiniClip records text only; it does not store images, files, or rich text.

## Data and privacy

- History is stored locally as **plain-text JSON**. MiniClip does not detect passwords or verification codes; copied secrets may therefore be recorded.
- When the application directory is writable, history, settings, and logs are stored in `data\` beside `MiniClip.exe`. Otherwise, MiniClip falls back to `%LOCALAPPDATA%\MiniClip\` and shows a notice.
- MiniClip does not provide cloud sync or telemetry. Diagnostic logs do not contain clipboard text.
- Uninstalling the installed version deletes its `data\` directory and `%LOCALAPPDATA%\MiniClip\`. Back up any history you want to keep. File deletion is not secure erasure.
- The portable version has no uninstaller. Removing its directory also removes history stored there. If MiniClip fell back to `%LOCALAPPDATA%\MiniClip\`, remove that directory separately if needed.

## Known limitations

- A non-elevated MiniClip process cannot inject a paste shortcut into an administrator-level window. You can paste manually with `Ctrl + V` in that case.
- Some apps do not expose text cursor coordinates. The popup then appears near the focused control or window. Some custom controls, games, and secure desktops may reject simulated paste input.
- Only one MiniClip instance can run at a time.

## Repository layout

| Path | Contents |
| --- | --- |
| `src/MiniClip/` | WPF application source and assets. |
| `installer/` | Inno Setup configuration. |
| `tools/` | Installer build and icon generation scripts. |
| `docs/BUILD.md` | Build and release instructions (Chinese). |
| `docs/VERIFICATION.md` | Self-test coverage and manual checks (Chinese). |
| `docs/DESIGN.md` | Current interface design and screenshots (Chinese). |

The repository contains source code, documentation, and the images needed to present the app. Installers, portable packages, and local test outputs are distributed through GitHub Releases or generated locally; they are not tracked in Git.
