# MiniClip Short Menu and Compact Windows Implementation Plan

> **For agentic workers:** This plan is executed inline in the current workspace because the user already authorized implementation. Checkboxes track completed work.

**Goal:** Keep two useful tray actions in a short menu, move secondary controls into Settings, and make the history popup and tray menu narrower with a brighter light surface.

**Architecture:** `TrayMenuBuilder` remains the single source of menu entries, while `TrayIconHost` routes Settings, theme, clear, and exit. The existing `HotkeySettingsWindow` gains a data section. Clearing history requires a small themed confirmation dialog before calling the existing controller method. The popup and tray menu use 220 DIP widths with 4 DIP selection gutters.

**Tech Stack:** .NET 10, WPF, Win32 tray icon.

**Spec:** User-approved short-menu layout and 232 DIP comparison in `design/narrow-232-preview.html`, followed by the user's instruction to make both windows smaller and the light background brighter.

## Global Constraints

- Preserve clipboard keyboard focus, history order, hotkey handling, and persistent settings.
- Keep monochrome light/dark themes and the current compact row heights.
- The active Release executable must be rebuilt and restarted after verification.

---

### Task 1: Menu layout and command routing

**Files:** `src/MiniClip/Tray/TrayMenuBuilder.cs`, `TrayMenuEntry.cs`, `TrayIconHost.cs`, `src/MiniClip/App.xaml.cs`, `src/MiniClip/Diagnostics/SelfTest.cs`.

- [x] Make the self-test require status, clear history, theme toggle, Settings, and Exit only; confirm empty history disables Clear and failed hotkey keeps Settings available.
- [x] Run the Debug self-test and verify this menu check fails against the current menu.
- [x] Replace the old flat entries and route `CommandSettings` to the existing settings window.
- [x] Run the Debug self-test and verify menu checks pass.

### Task 2: Settings data section and clear confirmation

**Files:** `src/MiniClip/UI/HotkeySettingsWindow.xaml`, `.xaml.cs`, new `UI/ClearHistoryConfirmationWindow.xaml`, `.xaml.cs`, `src/MiniClip/App.xaml.cs`, `src/MiniClip/Diagnostics/SelfTest.cs`.

- [x] Add self-test checks for Settings history count/folder command and both confirmation outcomes; verify they fail before implementation.
- [x] Add the history count, open-folder button, and local plain-text note to Settings; keep hotkey/startup Save behavior.
- [x] Show a themed confirmation dialog for Clear; only an explicit confirm invokes the existing history deletion.
- [x] Run the focused checks and verify all pass.

### Task 3: Width, light tone, and delivery

**Files:** `src/MiniClip/UI/PopupWindow.xaml`, `TrayMenuWindow.xaml`, `PopupThemePalette.cs`, `ChromeThemePalette.cs`, `src/MiniClip/Diagnostics/SelfTest.cs`, `design/MINIMAL-THEMES.md`, `README.md`.

- [x] Set history popup and tray menu to 220 DIP and their selected-row gutters to 4 DIP; preserve text alignment.
- [x] Set light surfaces to `#FDFDFD` while retaining the existing neutral selection and border colors.
- [x] Build Debug and Release; run full Release self-test and inspect light/dark and 150% screenshots.
- [x] Update design documentation and restart the Release instance; verify the active process path.
