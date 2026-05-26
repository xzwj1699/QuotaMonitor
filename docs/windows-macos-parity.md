# Windows and macOS Feature Parity

The target is one Avalonia desktop app backed by `QuotaMonitor.Core`. The legacy Windows Forms app remains only as the reference implementation until the Avalonia build is verified on Windows.

## Current parity map

| Area | Legacy Windows Forms | Avalonia Windows/macOS | Status |
| --- | --- | --- | --- |
| Quota readers | Realtime Codex/Claude with local fallback | Shared in `QuotaMonitor.Core` | Done |
| Config and history | JSON config and history snapshots | Shared in `QuotaMonitor.Core` with platform-aware data dirs | Done |
| Codex/Claude panels | 5h plus Week/7d quota bars | Same windows and plan labels | Done |
| Pace graph | Ideal line vs recorded usage pace | Same logic in `PaceChartControl` | Done |
| History graph | Day/Week/Month quota usage bars | Same aggregation in `HistoryChartControl` | Done |
| Service visibility | Show Codex / Show Claude | Settings, right-click menu, status menu | Done |
| Compact mode | Hides charts and shrinks layout | Same behavior in settings and menus | Done |
| Right-click menu | Refresh, minimize, visibility, compact, topmost, diagnostics, settings, config, exit | Same plus chart/theme/data actions | Done |
| Tray/menu bar | Windows `NotifyIcon` context menu and double-click restore | Avalonia `TrayIcon`/`NativeMenu`; menu actions work cross-platform | Mostly done |
| Alerts | Windows tray balloon alerts | Avalonia in-window notifications plus native macOS fallback when hidden | Mostly done |
| Start with system | Windows Run key | Windows Run key, macOS LaunchAgent, Linux autostart adapter | Done, needs Windows validation |
| Theme | Light/dark WinForms theme | Light/dark Avalonia palette | Done |
| Top-right startup | Places window at primary screen top-right | Same setting in Avalonia | Done |
| Diagnostics | Dialog with source/fallback/API details | Diagnostics window with shared details | Done |
| Self-test snapshot | Legacy app `--self-test` writes a snapshot file | `QuotaMonitor.Cli` writes the same snapshot output through shared core | Done with different entrypoint |
| Publish | `build.ps1`/`build.bat` for legacy `.exe` | macOS app bundle plus Windows self-contained publish scripts | Done |
| Tray tooltip | Concise quota summary in `NotifyIcon.Text` | Concise quota summary in Avalonia tray tooltip | Done |

## Known differences to close before removing WinForms

- Verify the Avalonia app on a real Windows machine, especially tray icon behavior, startup registration, notification presentation, window placement, and right-click menu behavior.
- Decide whether to keep the old `--self-test` argument on the GUI executable or treat `QuotaMonitor.Cli` as the supported diagnostic entrypoint.
- After Windows validation passes, remove the legacy Windows Forms source and old build scripts so the repository has one UI system.

## Windows Avalonia publish

From Windows PowerShell:

```powershell
.\scripts\publish-windows.ps1 win-x64
```

From macOS or Linux when cross-publishing a Windows build:

```bash
bash scripts/publish-windows.sh win-x64
```

The output is copied to:

```text
dist/windows-win-x64/QuotaMonitor.exe
```
