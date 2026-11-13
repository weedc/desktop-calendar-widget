# Build and validation

This release contains desktop widget source and generic documentation. Calendar data, account identifiers, local verification logs, and old prototypes are excluded. The sole image exception is root `ex.png`, explicitly reviewed and approved for publication by its owner; other screenshots remain excluded.

## macOS port

### Current release verification (2026-11-12)

Mac 5.8.1 has published Apple Silicon and Intel bundles. The [Intel job](https://github.com/weedc/desktop-calendar-widget/actions/runs/43176846220/job/131043067368) and [Apple Silicon job](https://github.com/weedc/desktop-calendar-widget/actions/runs/43176846220/job/131043067598), built from `64d40db`, each ran 62 Python tests: 61 passed and one was skipped. Native synthetic UI checks, app-bundle verification and bundled CLI/startup checks also passed. Windows 5.8.2 (`6f2828a`) does not change the Mac sources or Mac workflow.

The skipped test was `test_c_real_hidden_window_reuses_without_visibility_or_recreation`: neither runner exposed a usable Chrome GUI window. This is not a passing window-visibility test. Actual signed-in Google Calendar synchronization, repeated refresh without visible helper windows, and focus behavior remain **unverified on Mac**. No signed-in Mac is currently available for completing these checks; a future run must use the user's existing Chrome login and normal macOS permissions, without copying Windows credentials or disabling platform protections.

On that Mac, verify initial account connection and event reads, three manual refreshes, next-month/return navigation, disabled automatic synchronization with explicit refresh, and connection-loss cache preservation. Observe native window visibility and focus throughout; only initial connection or an explicit reconnection may create a browser window. Report counts and outcomes without publishing account identifiers or calendar text.

The separate `macos/` application uses Qt widgets and custom calendar painting, AppKit desktop window levels/all-Spaces behavior, a menu bar icon, local UserNotifications, and user-owned LaunchAgent startup. The native Windows application and platform-specific installer remain separate.

Run `python3 -m unittest discover -s macos -p test_core.py -v` for model tests and `python3 macos/main.py --smoke-test` for synthetic UI checks. The macOS workflow builds both Apple Silicon and Intel apps, verifies signatures and packaged MCP framing, and exercises actual desktop window levels, all-Spaces flags, menu bar visibility, staged checkboxes, Apply/Cancel behavior, day-panel reuse, local notes, highlights, and manual emoji protection. These checks do not use Google credentials or real calendars.

The Chrome adapter reads official Google Calendar accessible DOM labels using an owned Chrome window and user-authorized Apple Events. Live macOS Google login, private calendar access, notification permission/visible delivery, and Mission Control/Stage Manager transitions require a signed-in Mac and are not represented as completed by synthetic tests. Site markup changes may require adapter maintenance. Bundles are ad-hoc signed, without Developer ID notarization. macOS 14 or later is required.

The Mac adapter compiles real Chrome AppleScript against the installed Chrome dictionary and checks that a failed read does not launch a stopped Chrome. The workflow installs Chrome if absent so these native checks are not silently skipped for lack of its dictionary. Live private-account login requires a signed-in Mac; hidden-window rendering and focus transitions require an available Chrome GUI session. Model tests and script compilation alone do not establish those outcomes.

For the 5.7 adapter, [native Intel and Apple Silicon CI](https://github.com/weedc/desktop-calendar-widget/actions/runs/43158801149) passed 51 Python tests, native Qt smoke checks, app signing/bundle checks and packaged MCP checks on each architecture. One opt-in real-Chrome window-lifetime test was skipped on each runner: macOS first-launch verification did not expose a usable Chrome window, even after waiting. The disposable CI screenshot showed CoreServicesUIAgent verifying Chrome. No Gatekeeper, quarantine or Automation settings were changed. Actual hidden Chrome refresh/visibility behavior on Mac therefore remains unverified; it is not included in the passing claims. [Windows CI](https://github.com/weedc/desktop-calendar-widget/actions/runs/43158803634) also passed. The Windows live-account checks below were performed separately on the installed application.

## Reproducible checks

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
python tests/run_self_checks.py
python tests/run_mcp_checks.py
```

The 77 isolated native checks use temporary synthetic data. They cover setup migration, notes and time parsing, exclusive account/ID modes, Codex configuration preservation, hourly scheduling and disabled polling, responsive emoji layouts and Unicode sequences, manual/project overrides, startup ownership, daily Windows notification times and duplicate guards, partial-read cache preservation, and real checkbox selection binding. No live Google connection is required. CI runs these checks and validates all 26 MCP tool schemas and protocol framing.

With an already-running widget, `python tests/run_mcp_checks.py --live` also checks seven MCP routes, cache-only queries, and stale classification rejection. It does not print event contents.

The simplified setup and advanced dialog are additionally checked using synthetic native-form renders, isolated apply/cancel tests, and real settings-window interactions. The 5.7 existing-Chrome account discovery populated the calendar checklist without another login, preserved the selected calendar, and reconnected only after Apply. Cancel/apply and selection preservation remain covered by the native tests. Live tests confirmed that advanced values stay pending until final Apply. Compact, intermediate and short-wide window sizes used stickers or original text as specified. Settings merge tests preserve notes, emoji choices and notification records changed while the dialog was open.

## Windows integration checks performed

| Area | Verified behavior |
| --- | --- |
| Google refresh | In 5.7 the existing Chrome login is reused. Only first connection creates a reader; subsequent refresh reloads the same owned frame without showing or activating it. Three live-account refreshes returned the complete occurrence set, with new document IDs and the same HWND. Next-month/return passed. Across 699 samples after initial connection, no reader frame was visibly on-screen or foreground. The installed GUI's refresh button was also clicked three times and reused its reader. |
| Sync and navigation | Hourly policy, disabled startup without launching Google, manual refresh while disabled, refresh bursts, latest month requests during a background read, and background queries without moving the visible month. |
| Sources | Actual native checkboxes save enabled/disabled states; unchecking all calendars preserves the last on-disk Google data. Rechecking restores and refreshes events. Account and ID settings remain separate. |
| Cache failures | Closing the installed GUI's reader followed by three refreshes did not create a replacement. Navigating to another cached month loaded its saved events, with calendar-cache bytes unchanged. Explicit settings reconnect restored reading. Partial failures retain only enabled failed sources; a successful empty calendar removes its old events. Cache replacement is atomic. |
| Emoji layouts | Original text and compact-only sticker layouts (no intermediate summary labels), independent disable switch, no Google reads caused by resizing, manual editor save, Codex override protection, and persistence after restart. |
| Desktop and tray | Real desktop child attachment, floating taskbar state, minimize/hide followed by pin/unpin, tray restore, startup migration, and normal geometry retained after quitting while minimized. Desktop repaint clears stale pixels after detachment. |
| Details and UI | Reusing the same details window across dates; readable emoji-editor labels; settings columns and time field; native calendar checkboxes rather than checkbox-shaped text. |
| Windows notifications | Generic test notification displayed. A temporary near-future daily time fired through the real widget timer and Windows notification callback; the user's chosen time was restored. No overnight wait was used. |
| Daily timing | Synthetic Seoul midnight/custom-time boundaries, disabled/duplicate behavior, empty days, long summary limits, restart persistence, and notification spacing. Editing the time explicitly reschedules; saving the same time retains the duplicate guard. |
| Privacy | Source, compiled executable and portable archive checked against local private identifiers, calendar text and credential patterns before publication. Screenshots and runtime data are excluded. |

## Local use and limits

The widget requires Windows x64, .NET Framework 4.x, and Chrome with its existing Google login that can access the chosen calendars. It reads the official calendar site's accessible DOM labels; changes to that site can require maintenance. Some special birthday calendars are excluded. Display IDs are local occurrence IDs, not Google API event IDs.

Daily summaries are Windows system notifications at the selected Asia/Seoul time (default 00:00). The PC must be awake and the widget running. Codex is optional for emoji classification and local controls; it is not the daily notification scheduler. Previously created Codex schedules must be canceled in Codex. Windows notification settings can affect their display.

The hourly interval is checked with controlled timestamps rather than waiting an hour in CI. Live Google behavior and desktop integration are Windows integration checks, not cloud CI assertions. Existing personal preferences are restored after testing. No live-calendar writes are performed.

Public binaries are unsigned. The workflow packages a portable ZIP; the installer creates a desktop shortcut and upgrades only this application's existing startup entry. Personal data stays in the current Windows user's application-data folder.

## Retained existing-Chrome connection (5.7)

The initial connection may show its own Chrome reader. Windows uses UI Automation to read official accessible labels, then keeps the owned frame transparent and off-screen. Refresh uses the standard browser-refresh app command; it never closes/relaunches the reader. Month movement invokes the existing document's navigation. Per-window ownership properties prevent a reused HWND from being treated as the old reader. An explicit settings reconnect creates a new connection lease; a failed, missing, or disposed lease cannot authorize another background launch.

Mac uses the already-running Chrome's login and one owned window across calendars and months. It hides the initial window after the first complete snapshot and changes only that hidden window's URL for subsequent reads. Scope checks precede every background Apple Event: Chrome must already be running, the window must exist, and the URL/DOM owner tag must match. Missing or taken-over windows, stopped Chrome, and permission/read failures never create or restore windows. Only an explicit reconnect resets the lease. The headless profile, CDP endpoint and background-host entry have been removed from both platforms.

Windows validation: 77 isolated native checks, 14 update checks, MCP framing and 26 tool schemas plus seven live routes, actual existing-account calendar discovery (existing selection preserved, no repeated login), and the live-calendar refresh/navigation/connection-loss checks above. The installed executable matches the tested build. Personal cache, notes, emoji and notification options are preserved; read failures retain cache. Test harnesses/logs remain outside the repository and portable archive.

Python lifecycle tests cover initial creation, repeated refresh, multiple calendars, missing windows, Chrome shutdown, user-taken windows, initial failures, explicit reset, overflow handling and permissions. Native macOS script compilation, stopped-Chrome behavior and platform UI/bundle checks are reported separately from a real signed-in Mac account test. No tests write Google events or copy browser credentials.

## Startup defaults and cold refresh (5.7.1)

Fresh Windows installs enable login startup and create the desktop shortcut. A saved opt-out remains off on upgrades; explicit installer switches override that choice. The first setup also defaults to enabled when no earlier choice exists. Three synthetic startup-policy checks cover these cases.

Manual refresh, queued refresh, month movement and ordinary background requests cannot authorize a first browser window. Only the startup connection and an explicit settings connection may do so. This closes the case where synchronization was disabled at launch and the first manual refresh previously opened Chrome. Both platform adapters implement the gate.

The isolated Windows build passed 77 native checks, 14 update checks and MCP framing/schema checks. The Python suite passed 51 portable tests with three native-Mac checks skipped on Windows. The installed 5.7.1 application was restarted as the actual Windows user. Three cold refreshes kept the reader absent and calendar cache unchanged. A second launch used the registered login-startup executable and completed its initial connection; three subsequent refreshes returned fresh nonempty snapshots in the same hidden reader. Across 150 native create/show/focus events, no related window became visibly on-screen during either refresh phase. Startup registration, Windows startup-approval state, desktop shortcut and final visible widget were checked. Other applications’ startup entries were preserved. This checks the registered command and a real restart, rather than simulating a Windows logoff. The installed binary, desktop-shortcut target and previous startup executable target match the tested build. Personal settings are preserved, including the original automatic-sync preference after testing.

## Windows popup and owner colors (5.8.3)

The Windows rebuild passed 82 native self-checks, 22 update checks, 27 resize checks, and MCP framing with 26 tool schemas. New checks cover owner grouping, preservation through reconnection and staged settings, cached-event color resolution, popup ownership during activation changes, and rounding on first handle creation. A synthetic native window was rendered and dismissed through activation of a separate native window; no real calendar data was used.

The calendar list exposes a color/owner editor for each source. Unknown shared-calendar owners are assigned explicitly; the widget does not infer a Google owner from a group calendar ID. Display-only settings do not replace the Chrome reader. Outside dismissal cancels unapplied settings; notes retain their existing autosave behavior. Active Google login is kept open. Month selection now uses the same nonmodal popup and theme.

The final 5.8.3 executable was installed and started as the Windows user. Its real settings UI displayed the new per-source color/owner buttons. A signed-in Google refresh completed successfully, reused the same hidden reader, and remained visible as a desktop widget. Native create/show/focus monitoring during that refresh recorded no unexpected visible helper windows. Login startup and the desktop shortcut now target the installed 5.8.3 executable. The V3 protection settings were not changed. These UI changes are Windows-specific; the Mac release and signed-in Mac verification limits above remain unchanged.
