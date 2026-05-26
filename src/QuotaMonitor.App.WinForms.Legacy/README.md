# QuotaMonitor.App.WinForms.Legacy

This project keeps the existing Windows Forms/.NET Framework application available while the cross-platform Avalonia shell is built.

It is intentionally temporary. New business logic should move to `QuotaMonitor.Core`; once the Avalonia application reaches feature parity on Windows and macOS, this project and the root `QuotaMonitorNetFx.cs` entrypoint should be removed.
