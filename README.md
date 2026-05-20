# Quota Monitor

A small Windows desktop monitor for Codex and Claude Code quota.

## Features

- Codex 5h and weekly quota bars.
- Claude 5h and 7d quota bars.
- Optional Codex-only or Claude-only view.
- Subscription type display for each visible service.
- Realtime API first, local-log fallback.
- Week/7d usage pace chart: dashed ideal line vs actual recorded usage.
- Day/week/month usage history chart: bars show period quota consumed, line shows cumulative consumed quota.
- Auto refresh every 5 minutes by default.
- Resizable window, optional topmost mode, and standard minimize support.
- No .NET SDK required; builds with the Windows built-in .NET Framework compiler.

## Run

Double-click:

```text
QuotaMonitor.exe
```

Or:

```powershell
.\QuotaMonitor.exe
```

Use the top controls to switch between `Pace` and `History`. In `History`, choose `Day`, `Week`, or `Month`.
Right-click the window to switch Codex/Claude visibility, toggle topmost mode, refresh, minimize, or exit.

## Data Sources

Realtime mode is enabled by default:

- Codex: reads `%USERPROFILE%\.codex\auth.json`, then calls `https://chatgpt.com/backend-api/wham/usage`.
- Claude: reads `%USERPROFILE%\.claude\.credentials.json`, then calls `https://api.anthropic.com/api/oauth/usage`.

Fallback mode:

- Codex: reads `%USERPROFILE%\.codex\sessions` `token_count` events.
- Claude: reads `%USERPROFILE%\.claude\projects` JSONL usage records and estimates usage.

The app does not print or persist OAuth token values. It records quota history in `quota-monitor-history.jsonl` with only service, window, timestamp, used percent, reset time, and window length.

History charts use the long quota windows by default: Codex `Week` and Claude `7d`. Period usage is calculated from positive `used_percent` deltas between recorded samples.

## Build

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

## Configuration

On first run the app creates:

```text
quota-monitor.config.json
```

You can copy from `quota-monitor.config.example.json`.

Important settings:

- `pollIntervalSeconds`: refresh interval. Default is `300`.
- `alwaysOnTop`: keep the monitor above other windows.
- `startAtTopRight`: place the window at the top-right of the primary screen.
- `showCodex`: show or hide the Codex column.
- `showClaude`: show or hide the Claude column.
- `useRealtimeApi`: use Codex/Claude realtime endpoints first.
- `realtimeApiTimeoutSeconds`: request timeout.

Generated runtime files are ignored by git:

- `quota-monitor.config.json`
- `quota-monitor.log`
- `quota-monitor.snapshot.txt`
- `quota-monitor-history.jsonl`

## Notes

The realtime endpoints are product-login endpoints used by Codex/Claude Code tooling, not public billing Admin APIs. They are more accurate for subscription quota than local log estimates, but they may change. Local fallback is kept for resilience.
