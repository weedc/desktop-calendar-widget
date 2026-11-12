# Build and validation

This release contains desktop widget source and generic documentation. Calendar data, account identifiers, local verification logs, and old prototypes are excluded. The sole image exception is root `ex.png`, explicitly reviewed and approved for publication by its owner; other screenshots remain excluded.

## macOS port

The separate `macos/` application uses Qt widgets and custom calendar painting, AppKit desktop window levels/all-Spaces behavior, a menu bar icon, local UserNotifications, and user-owned LaunchAgent startup. The native Windows application and platform-specific installer remain separate.

Run `python3 -m unittest discover -s macos -p test_core.py -v` for model tests and `python3 macos/main.py --smoke-test` for synthetic UI checks. The macOS workflow builds both Apple Silicon and Intel apps, verifies signatures and packaged MCP framing, and exercises actual desktop window levels, all-Spaces flags, menu bar visibility, staged checkboxes, Apply/Cancel behavior, day-panel reuse, local notes, highlights, and manual emoji protection. These checks do not use Google credentials or real calendars.

The Chrome adapter reads official Google Calendar accessible DOM labels using headless Chrome and a loopback-only CDP connection. Live macOS Google login, private calendar access, notification permission/visible delivery, and Mission Control/Stage Manager transitions require a signed-in Mac and are not represented as completed by synthetic tests. Site markup changes may require adapter maintenance. Bundles are ad-hoc signed, without Developer ID notarization. macOS 14 or later is required.

A fresh, unsigned-in headless Chrome session also exercised the exact DOM extraction program against an official public calendar: 35 date cells and five November 2026 occurrences were read. This caught and corrected the distinction between `aria-label`, `aria-labelledby`, and hidden accessible event text. This is a public Google DOM check on Windows, not a claim of live macOS account login validation. The fetched pages and disposable browser profiles are not bundled. Native self-checks contain only synthetic calendar IDs.

## Reproducible checks

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
python tests/run_self_checks.py
python tests/run_mcp_checks.py
```

The 73 isolated native checks use temporary synthetic data. They cover setup migration, notes and time parsing, exclusive account/ID modes, Codex configuration preservation, hourly scheduling and disabled polling, responsive emoji layouts and Unicode sequences, manual/project overrides, startup ownership, daily Windows notification times and duplicate guards, partial-read cache preservation, and real checkbox selection binding. No live Google connection is required. CI runs these checks and validates all 26 MCP tool schemas and protocol framing.

With an already-running widget, `python tests/run_mcp_checks.py --live` also checks seven MCP routes, cache-only queries, and stale classification rejection. It does not print event contents.

The simplified setup and advanced dialog are additionally checked using synthetic native-form renders, isolated apply/cancel tests, and real settings-window interactions. The earlier connection adapter populated the inline checklist; its Google-login result does not validate the new 5.6 dedicated-profile adapter. Cancel/apply and selection preservation remain covered by the native tests. Live tests confirmed that advanced values stay pending until final Apply. Compact, intermediate and short-wide window sizes used stickers or original text as specified. Settings merge tests preserve notes, emoji choices and notification records changed while the dialog was open.

## Windows integration checks performed

| Area | Verified behavior |
| --- | --- |
| Google refresh | In 5.6, background startup and all reads use a dedicated headless process with no Win32 reader frame. Public-calendar repeated refresh and month navigation passed without new visible or active Chrome windows. See windowless validation below; private-login verification is separate. |
| Sync and navigation | Hourly policy, disabled startup without launching Google, manual refresh while disabled, refresh bursts, latest month requests during a background read, and background queries without moving the visible month. |
| Sources | Actual native checkboxes save enabled/disabled states; unchecking all calendars preserves the last on-disk Google data. Rechecking restores and refreshes events. Account and ID settings remain separate. |
| Cache failures | The last available data remains visible after a read failure. Partial failures retain only enabled failed sources; a successful empty calendar removes its old events. Cache replacement is atomic. |
| Emoji layouts | Original text and compact-only sticker layouts (no intermediate summary labels), independent disable switch, no Google reads caused by resizing, manual editor save, Codex override protection, and persistence after restart. |
| Desktop and tray | Real desktop child attachment, floating taskbar state, minimize/hide followed by pin/unpin, tray restore, startup migration, and normal geometry retained after quitting while minimized. Desktop repaint clears stale pixels after detachment. |
| Details and UI | Reusing the same details window across dates; readable emoji-editor labels; settings columns and time field; native calendar checkboxes rather than checkbox-shaped text. |
| Windows notifications | Generic test notification displayed. A temporary near-future daily time fired through the real widget timer and Windows notification callback; the user's chosen time was restored. No overnight wait was used. |
| Daily timing | Synthetic Seoul midnight/custom-time boundaries, disabled/duplicate behavior, empty days, long summary limits, restart persistence, and notification spacing. Editing the time explicitly reschedules; saving the same time retains the duplicate guard. |
| Privacy | Source, compiled executable and portable archive checked against local private identifiers, calendar text and credential patterns before publication. Screenshots and runtime data are excluded. |

## Local use and limits

The widget requires Windows x64, .NET Framework 4.x, and Chrome with a dedicated widget Google login that can access the chosen calendars. It reads the official calendar site's accessible DOM labels; changes to that site can require maintenance. Some special birthday calendars are excluded. Display IDs are local occurrence IDs, not Google API event IDs.

Daily summaries are Windows system notifications at the selected Asia/Seoul time (default 00:00). The PC must be awake and the widget running. Codex is optional for emoji classification and local controls; it is not the daily notification scheduler. Previously created Codex schedules must be canceled in Codex. Windows notification settings can affect their display.

The hourly interval is checked with controlled timestamps rather than waiting an hour in CI. Live Google behavior and desktop integration are Windows integration checks, not cloud CI assertions. Existing personal preferences are restored after testing. No live-calendar writes are performed.

Public binaries are unsigned. The workflow packages a portable ZIP; the installer creates a desktop shortcut and upgrades only this application's existing startup entry. Personal data stays in the current Windows user's application-data folder.

## Windowless Google connection (5.6)

Windows and macOS now share a fixed DOM program embedded in the Windows executable and bundled in the Mac app. Routine reads launch only `--headless=new` with a dedicated app-data profile and an ephemeral loopback CDP endpoint. Explicit login uses the same profile in visible Chrome without remote debugging. Existing Chrome profiles are neither attached to nor copied. No login-form fields, cookies or tokens are extracted. Automatic failure/recovery never opens an interactive window.

The former Windows UI Automation warmup (on-screen/topmost/layered window followed by desktop reparenting), Chrome frame-recreation refresh, and macOS AppleScript window creation/minimization/restoration paths have been removed. Mac no longer requests Apple Events permission. A Windows kill-on-close job and a Mac parent-pipe guard prevent orphaned headless processes holding the profile after the widget exits. Login windows are left under the user's control. Both adapters also detect a live process using the dedicated profile before a new launch, including after a widget restart, so Chrome cannot forward a background URL into a leftover visible login window. Real metadata checks confirmed both adapters refuse that launch.

Validation on Windows: 73 native checks; 40 Python model/connection checks; MCP framing and 26 tool schemas. The native reader and Python Mac adapter each read a real public Google holiday calendar three times, retaining one background browser, and returned the expected five November occurrences. Native next-month/return and repeated fresh documents passed. Sampling from initial native browser startup found no new visible or active Chrome frames. Python parent-pipe closure terminated the owned Chrome and a later read recovered in headless mode. Abruptly killing the native test host also terminated its Chrome through the job object; the separately opened interactive login was unaffected. These checks used disposable profiles and public data, not production credentials.

A fresh dedicated private-account login and calendar discovery must be verified with the user's explicit authentication. Native macOS login/window behavior still requires a Mac; Python adapter network checks on Windows do not establish native Mac UI behavior. Existing caches and personal data are retained across migration. The release is not published by these local checks.
