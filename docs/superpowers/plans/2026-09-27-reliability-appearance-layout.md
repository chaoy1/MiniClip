# MiniClip Reliability, Appearance, and Layout Implementation Plan

> **For agentic workers:** The user approved the previous review findings and directly requested the UI changes. Execute in this workspace, recording verification at each stage.

**Goal:** Protect saved history on failures, make operational failures visible, add a three-choice appearance submenu, and precisely fit five history rows.

**Architecture:** The controller owns one settings instance and a guarded history-write state. Appearance preference (Light, Dark, System) is persisted separately from the resolved light/dark palette. The tray menu exposes an appearance submenu; the history ScrollViewer has fixed external vertical padding so its five-row viewport cannot show a clipped sixth row.

**Tech Stack:** .NET 10 WPF, Win32 clipboard/hotkey, Windows app-theme registry and `SystemEvents`.

**Spec:** User screenshots in the current chat: menu edge marked at about 184 DIP, popup top overflow and selection touching the top edge. Previous review findings in the immediately preceding assistant response.

## Global Constraints

- Preserve `WS_EX_NOACTIVATE`, caret positioning, oldest-to-newest visual order, and 100-entry/500,000-character bounds.
- Menu stays monochrome with 26 DIP rows; status, clear, appearance, settings, exit remain on the root.
- Use an explicit light/dark/System preference; System follows `AppsUseLightTheme` and updates when Windows preferences change.
- Do not overwrite history after an unreadable file or failed quarantine until the original is safely preserved.
- Failure of clearing, clipboard listener, keyboard hook, hotkey persistence, or startup registration must be visible to the user.

---

### Task 1: History and settings safety

**Files:** `MiniClipController.cs`, `App.xaml.cs`, `SettingsStore.cs`, `JsonStorage.cs`, `Diagnostics/SelfTest.cs`.

- [x] Check failed history load blocking writes, clear failure reporting, and privacy-notice state across later settings saves.
- [x] Guard history persistence after an unreadable file; preserve a corrupt original before writes.
- [x] Route settings writes through the controller, check save outcomes, and report failures without claiming saved state.
- [x] Make clear-history deletion return an outcome that the UI displays; repair the exit-time async flush path.

### Task 2: Operational failures and privacy

**Files:** `MiniClipController.cs`, `App.xaml.cs`, `Interop/Win32Focus.cs`, `Diagnostics/SelfTest.cs`.

- [ ] Failure injection for listener/hook installation remains a manual desktop check; the normal listener and hook paths passed the Win32 self-test.
- [x] Stop startup when clipboard listening or tray creation fails; close an unusable popup when the hook fails.
- [x] Record only process ID in popup diagnostics, not a document title.
- [x] Prevent repeated identical notices on every history change.

### Task 3: Appearance submenu

**Files:** `UI/AppearancePreference.cs`, `MiniClipController.cs`, `Tray/TrayMenuEntry.cs`, `Tray/TrayMenuBuilder.cs`, `UI/TrayMenuWindow.xaml(.cs)`, `Tray/TrayIconHost.cs`, `App.xaml.cs`, `Diagnostics/SelfTest.cs`.

- [x] Check Light/Dark/System persistence and resolution, submenu entries, root placement, and command dispatch.
- [x] Persist the preference, resolve the Windows app theme, and update the popup after a system theme change.
- [x] Render a rounded monochrome appearance submenu with current-choice mark, mouse and keyboard access.
- [x] Set the root menu to 184 DIP and inspect labels/edge at 150% scaling.

### Task 4: Five-row popup and performance

**Files:** `UI/PopupWindow.xaml(.cs)`, `Diagnostics/SelfTest.cs`, design screenshots and docs.

- [x] Check exactly five visible rows, no top overflow, and top/right selection inset after scrolling; inspect bottom inset in screenshots.
- [x] Move vertical padding outside the scrolling content; keep overlay scrollbar and full-width selection.
- [x] Inspect screenshots for newest, oldest, and scrolling selection in both palettes.
- [x] Measure startup and repeated popup memory/latency; document that the sample exceeds the 100 ms target.
- [x] Update project plan, README, visual spec, verification docs, and final screenshots.

### Task 5: Release verification

- [x] Stop only the running MiniClip Release instance; build Debug and Release.
- [x] Run full self-test with exit code 0 and inspect artifact screenshots.
- [x] Restart Release hidden and verify exact process path and startup log.
