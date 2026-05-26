# Windows and macOS Migration Plan

The target architecture is one maintained application stack:

```text
QuotaMonitor.Core
  Shared quota readers, config, history, alerts, diagnostics, path policy.

QuotaMonitor.App.Avalonia
  The future single desktop UI for Windows and macOS.

QuotaMonitor.App.WinForms.Legacy
  Temporary Windows Forms shell kept only until the Avalonia app reaches parity.
```

## Phase 1: shared core

`QuotaMonitor.Core` is the only place for business behavior going forward. New work should move quota reading, API parsing, local log parsing, history, alert evaluation, and diagnostics here.

Runtime files now have a platform-aware home:

- Windows: `%APPDATA%\QuotaMonitor`
- macOS: `~/Library/Application Support/QuotaMonitor`
- Linux/other: `$XDG_CONFIG_HOME/QuotaMonitor` or `~/.config/QuotaMonitor`

For compatibility, `MonitorConfig.LoadOrCreate` can import an old `quota-monitor.config.json` from the legacy executable directory when the new config file does not exist yet.

## Phase 2: Avalonia app

`QuotaMonitor.App.Avalonia` is the cross-platform shell and is wired to `QuotaMonitor.Core`. Platform-specific behavior should be hidden behind small adapters:

- startup registration (`StartupRegistration`)
- system tray/menu bar (`TrayIcon` and `NativeMenu`)
- notifications
- file launching

No quota-reading or history logic should be implemented in the UI project.

The current macOS preview can be packaged as a normal app bundle:

```bash
bash scripts/publish-macos.sh osx-arm64
open "dist/Quota Monitor.app"
```

The Avalonia Windows build can be published as a self-contained app:

```powershell
.\scripts\publish-windows.ps1 win-x64
```

Feature parity is tracked in `docs/windows-macos-parity.md`.

## Phase 3: remove legacy Windows Forms

When the Avalonia app supports the existing Windows workflows and the macOS workflows, and the parity checklist has no blocking items, remove:

- `QuotaMonitorNetFx.cs`
- `build.ps1`
- `build.bat`
- `Start-QuotaMonitor.vbs`
- `src/QuotaMonitor.App.WinForms.Legacy`

At that point the repository has one UI system and one core system.
