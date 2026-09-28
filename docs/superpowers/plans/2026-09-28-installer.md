# MiniClip Installer Implementation Plan

> **For agentic workers:** Execute inline because this workspace is not a Git repository and the user authorized the current session. Steps use checkbox syntax for tracking.

**Goal:** Build a self-contained Windows installer with selectable installation directory, optional startup, and complete removal of MiniClip-owned data at uninstall.

**Architecture:** Publish the existing WPF app as self-contained `win-x64` files. Inno Setup packages those files and owns installation, startup registration, shortcuts, and uninstall cleanup. A PowerShell wrapper makes the build repeatable.

**Tech Stack:** .NET 10 SDK, PowerShell, Inno Setup.

**Spec:** `docs/superpowers/specs/2026-09-28-installer-design.md`

## Global Constraints

- Per-user installation, no UAC requirement.
- Chinese wizard with black/white system-responsive appearance.
- User-selected install path; startup task controls the same HKCU Run value as the application.
- Uninstall removes `%LOCALAPPDATA%\MiniClip` and the Run value; it never recursively removes arbitrary `{app}` contents.
- No live-profile uninstall test.

---

### Task 1: Installer source and build wrapper

**Files:** Create `installer/MiniClip.iss`, `tools/build-installer.ps1`; modify `docs/BUILD.md`.

- [x] Write the Inno setup source with `AppMutex=Local\MiniClip.SingleInstance.v1`, `PrivilegesRequired=lowest`, a visible directory page, `[Tasks]` startup choice, `[Files]` from a publish directory, `[Icons]`, `[Run]`, and uninstall deletion of `{localappdata}\MiniClip`.
- [x] Build script resolves .NET SDK and ISCC, publishes with `-r win-x64 --self-contained true -p:PublishSingleFile=false`, then compiles `.iss` with explicit publish/output directories and confirms the output exists.
- [x] Document installation, silent install options, startup, uninstall data deletion, and the build command.

### Task 2: Compile and verify

**Files:** Installer output at `dist/MiniClip-Setup-1.0.0-x64.exe`; verification output under `artifacts/`.

- [x] Obtain an official Inno Setup compiler and verify its publisher signature.
- [x] Run the wrapper and fix any compiler/build diagnostics.
- [x] Run the published app with `--run 2` and verify exit code zero. Full `--selftest` was skipped because it rewrites the system clipboard; the user's clipboard contents were preserved.
- [x] Install and uninstall in a temporary custom path with isolated test data; verify startup, Start Menu shortcut, uninstall cleanup, and restoration of the user's original MiniClip data.
