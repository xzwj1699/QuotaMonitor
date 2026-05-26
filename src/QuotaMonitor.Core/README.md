# QuotaMonitor.Core

Shared quota-monitoring logic for all future app shells.

This project should stay free of UI framework dependencies. It may read files, call quota APIs, store history, evaluate alerts, and format diagnostic data, but it should not reference Windows Forms, Avalonia, registry APIs, menu-bar APIs, or notification APIs directly.
