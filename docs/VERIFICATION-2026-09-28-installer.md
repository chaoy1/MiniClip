# Installer verification — 2026-09-28

## Artifact

- `dist/MiniClip-Setup-1.0.0-x64.exe`
- Size: 52,146,728 bytes
- SHA-256: `BBE31A9BEDE8DA799C8B5D061873AA7C21D51E3F2539984EF6CB479472D6B747`
- Source: `installer/MiniClip.iss`, built using `tools/build-installer.ps1` and Inno Setup 7.1.0.
- Official Inno Setup compiler download passed Authenticode validation; signer: `Pyrsys B.V.`. The MiniClip installer itself is not code-signed.

## Checks

| Check | Result |
| --- | --- |
| Self-contained `win-x64` publish and Inno compilation | Passed |
| Published `MiniClip.exe --run 2` | Exit code 0 |
| Silent installation to a custom path containing spaces | Passed |
| Startup task selected | HKCU Run value pointed to the chosen `MiniClip.exe` |
| Windows startup approval state | Installer cleared stale `StartupApproved\Run` and `Run32` values for MiniClip |
| Start Menu shortcut and uninstaller | Present after installation |
| Silent uninstall with isolated test data | Exit code 0 |
| Uninstall cleanup | Program directory, test data directory, startup value, startup approval values, Start Menu shortcut, and uninstall registry entry absent afterward |
| Fresh installation without the startup task | Run value remained absent |
| User's original MiniClip data and app | Data directory and startup value restored; original process running |

The uninstall smoke test temporarily moved the existing `%LOCALAPPDATA%\MiniClip` directory aside, created only dummy data at the original path, then restored the original directory after uninstall. No live history was deleted. The final package's test log is under `artifacts/installer/smoke-logs-final-ac27d418b26c4d798994f2065606a818`.

Two exploratory silent-install attempts with explicit task deselection (`/TASKS=` and `/MERGETASKS=!autostart`) exited with code 1 before writing a setup log. These command-line forms are not part of the documented MiniClip workflow. The fresh-install default-off path and explicit selected-task path both passed. Interactive deselection on an upgrade was not exercised by the silent smoke test.

The full application `--selftest` was not rerun on the self-contained publish because it overwrites the system clipboard. The timed run verified startup and shutdown without changing clipboard contents. A clean Windows computer without a preinstalled .NET runtime was not available for an independent environment test.
