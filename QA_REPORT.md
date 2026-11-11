# Build and validation

This public snapshot contains the desktop widget source and generic documentation. Local calendar data, account identifiers, verification logs, screenshots, and older prototypes are excluded.

## Requirements

- Windows x64 with .NET Framework 4.x/WPF assemblies.
- Google Chrome and a Google account with access to the calendars you choose.
- Optional: Codex desktop app for MCP controls and scheduled summaries.

## Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The build uses the Windows .NET Framework compiler and no downloaded NuGet/npm dependencies.

## Isolated self-checks

```powershell
python tests/run_self_checks.py
```

The 19 checks cover setup persistence and migration, time parsing, one-hour calculations, Codex configuration preservation/collision handling, and exclusive ID/account modes. They use temporary test data and do not connect to a live calendar. This is also the CI check.

## Manual integration checks

Run the widget after choosing your own connection. Verify account calendar selection, direct-ID selection, switching modes without mixing sources, refresh, month navigation, notes and highlights, native desktop attachment, hide/show, resizing, and first-run setup behavior.

For Codex, register the MCP connection through settings, restart the client if needed, then submit the reminder setup request from your own account. A prepared draft is not a completed automation. Confirm the actual automation in Codex and use the widget's registration-status button.

## Scope

The Google reader uses the official site's accessibility UI with your selected Chrome login. Google UI changes can require maintenance. Some special birthday calendars are not available through the supported display view. Cloud/Google API event IDs are not supplied by this reader.

Public binaries are unsigned. The GitHub workflow builds the application and packages a portable ZIP. Local data is stored under the current Windows user's application-data directory, and is never part of the release package.
