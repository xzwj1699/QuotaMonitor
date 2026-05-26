# Quota Monitor

A small Windows desktop monitor for Codex and Claude Code quota.

## Cross-platform migration status

This repository is being migrated toward one maintained Windows/macOS application.

- `src/QuotaMonitor.Core` contains the shared quota-reading, config, history, and alert logic for future Windows and macOS builds.
- `src/QuotaMonitor.App.Avalonia` is the new cross-platform desktop shell.
- `src/QuotaMonitor.App.WinForms.Legacy` keeps the existing Windows Forms app available during migration.
- The final target is a single cross-platform desktop shell that uses `QuotaMonitor.Core`; the legacy WinForms app is temporary.

The current macOS app includes refresh, settings, diagnostics, config/data directory shortcuts, compact mode, right-click window actions, menu bar status item actions, start-with-system registration, low-quota notifications, light/dark themes, Codex/Claude pace charts, and Day/Week/Month history charts.

See `docs/macos-windows-migration.md` for the migration plan and `docs/windows-macos-parity.md` for the feature parity checklist.

## Features

- Codex 5h and weekly quota bars.
- Claude 5h and 7d quota bars.
- Optional Codex-only or Claude-only view.
- Subscription type display for each visible service.
- Realtime API first, local-log fallback.
- Diagnostics view for source, fallback, login, and API errors.
- Week/7d usage pace chart: dashed ideal line vs actual recorded usage.
- Day/week/month usage history chart: bars show period quota consumed.
- Custom quota notifications for low 5h and Week/7d remaining quota.
- Pace charts estimate when quota will be exhausted at the current rate.
- Auto refresh every 5 minutes by default.
- System tray/menu bar mode with restore, refresh, settings, and exit actions.
- Resizable window, optional topmost mode, compact mode, and light/dark themes.
- Optional start with system.
- Legacy Windows builds do not require a .NET SDK; new cross-platform builds require the .NET 10 SDK.

## Run

Double-click:

```text
QuotaMonitor.exe
```

Or:

```powershell
.\QuotaMonitor.exe
```

Use the chart controls to switch between `Pace` and `History`. When `History` is active, choose `Day`, `Week`, or `Month`.
Right-click the window to switch Codex/Claude visibility, open diagnostics, open settings, toggle topmost mode, refresh, minimize, or exit. On macOS, use the menu bar status item for the same actions; on Windows, use the tray icon.

## Data Sources

Realtime mode is enabled by default. New cross-platform defaults use `~` for the user home directory; legacy Windows configs using `%USERPROFILE%` are still supported.

- Codex: reads `~/.codex/auth.json`, then calls `https://chatgpt.com/backend-api/wham/usage`.
- Claude: reads `~/.claude/.credentials.json`, then calls `https://api.anthropic.com/api/oauth/usage`.

Fallback mode:

- Codex: reads `~/.codex/sessions` `token_count` events.
- Claude: reads `~/.claude/projects` JSONL usage records and estimates usage.

The app does not print or persist OAuth token values. It records quota history in `quota-monitor-history.jsonl` with only service, window, timestamp, used percent, reset time, and window length.

History charts use the long quota windows by default: Codex `Week` and Claude `7d`. Period usage is calculated from positive `used_percent` deltas between recorded samples.

## Build

Current stable Windows build:

Use either:

```powershell
.\build.ps1
```

or:

```text
build.bat
```

Build output:

```text
QuotaMonitor.exe
```

New SDK-style projects:

```text
QuotaMonitor.sln
src/QuotaMonitor.Core
src/QuotaMonitor.Cli
src/QuotaMonitor.App.Avalonia
src/QuotaMonitor.App.WinForms.Legacy
```

These require the .NET 10 SDK. `QuotaMonitor.sln` contains the cross-platform projects only; the legacy WinForms project remains outside the solution because it is Windows-only.

Build the new cross-platform shell:

```bash
dotnet build src/QuotaMonitor.App.Avalonia/QuotaMonitor.App.Avalonia.csproj
```

Publish the new Avalonia app for Windows x64 from Windows PowerShell:

```powershell
.\scripts\publish-windows.ps1 win-x64
```

Or cross-publish the Windows build from macOS/Linux:

```bash
bash scripts/publish-windows.sh win-x64
```

The Windows script creates:

```text
dist/windows-win-x64/QuotaMonitor.exe
```

Use `win-arm64` instead of `win-x64` to produce an ARM64 Windows build.

Publish for Apple Silicon macOS:

```bash
bash scripts/publish-macos.sh osx-arm64
```

The script creates:

```text
dist/Quota Monitor.app
```

Open it from Finder, or run:

```bash
open "dist/Quota Monitor.app"
```

Publish for Intel macOS:

```bash
bash scripts/publish-macos.sh osx-x64
```

## Configuration

On first run the app creates:

```text
quota-monitor.config.json
```

Legacy Windows builds create this file beside `QuotaMonitor.exe`. New SDK-style builds create runtime files in the platform app-data directory, for example `~/Library/Application Support/QuotaMonitor` on macOS.

You can copy from `quota-monitor.config.example.json`.

Important settings:

- `pollIntervalSeconds`: refresh interval. Default is `300`.
- `alwaysOnTop`: keep the monitor above other windows.
- `startAtTopRight`: place the window at the top-right of the primary screen.
- `showCodex`: show or hide the Codex column.
- `showClaude`: show or hide the Claude column.
- `minimizeToTray`: keep the app running in the system tray/menu bar when the window is closed or minimized.
- `startWithWindows`: legacy Windows name for registering the app in the current user's Windows startup Run key.
- `startWithSystem`: cross-platform replacement used by the new Core configuration model.
- `compactMode`: hide charts and show a smaller quota-bar-only view.
- `theme`: `light` or `dark`.
- `alertsEnabled`: show low-quota notifications.
- `alertFiveHourRemainingPercent`: alert threshold for 5h quota.
- `alertLongWindowRemainingPercent`: alert threshold for Week/7d quota.
- `useRealtimeApi`: use Codex/Claude realtime endpoints first.
- `realtimeApiTimeoutSeconds`: request timeout.

Generated runtime files are ignored by git:

- `quota-monitor.config.json`
- `quota-monitor.log`
- `quota-monitor.snapshot.txt`
- `quota-monitor-history.jsonl`

## Notes

The realtime endpoints are product-login endpoints used by Codex/Claude Code tooling, not public billing Admin APIs. They are more accurate for subscription quota than local log estimates, but they may change. Local fallback is kept for resilience.
