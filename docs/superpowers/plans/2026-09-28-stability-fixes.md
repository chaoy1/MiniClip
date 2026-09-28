# MiniClip Stability Fixes Implementation Plan

**Goal:** Resolve the six review findings without changing MiniClip's compact interaction design.

**Architecture:** Keep the existing controller, popup, hook, storage and tray boundaries. Use a clipboard capture epoch to invalidate work predating a clear, a revision to track which history snapshot was saved, and a close generation to invalidate stale popup animations. Preserve the existing synchronous Windows clipboard API and JSON format.

**Tech Stack:** .NET 10, WPF, Win32 clipboard and keyboard hooks, the built-in `--selftest` harness.

## Constraints

- Keep history at 100 entries and 500,000 total characters.
- Do not log clipboard contents.
- Preserve the non-activating popup and keyboard focus.
- No new background polling or external dependencies.

## Tasks

- [x] Add failing self-tests for a clipboard read completing after clear and a saved older revision followed by a failed newer save. Then invalidate pre-clear captures and tie dirty state to revisions.
- [x] Add a failing self-test for closing and reopening the popup inside the fade duration. Cancel the old timer and ignore callbacks from older close generations.
- [x] Add a failing self-test for a modifier held across hook uninstallation. Clear the hook's tracking state when it is removed.
- [x] Add a failing self-test for a clipboard containing only registered `text/plain`. Read supported registered formats explicitly, with bounded decoding and no effect on standard Unicode text.
- [x] Add a failing self-test for a history file whose entries are all unusable. Keep the clear command available whenever an on-disk history file remains.
- [x] Extend theme parsing tests for unknown CloudStore payloads and verify the existing light, dark and system behavior.
- [x] Build Release and run the full self-test with a fresh output directory; inspect failures and fix only concrete regressions.

## Verification

Release build: 0 warnings, 0 errors. The final `--selftest` run returned exit code 0 and `result=pass` in `artifacts/review-green-queuedclear/selftest.log`. The updated tray process was restarted from the Release output directory.
