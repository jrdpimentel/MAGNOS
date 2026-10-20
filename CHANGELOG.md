# Changelog

## Unreleased
- Automated tests for pricing, session time maths, billing, and database upgrades (CI runs them on every push).
- Client code split into focused files (core, security, UI, native).
- Architecture, design-decision, and roadmap documents.

## 0.1.0 - first release
- Server (ASP.NET Core 8, SQLite, SignalR) running as a Windows service, with a web admin dashboard.
- Customer lock-screen client (WPF, .NET 8) with login, registration, session widget, chat, and staff exit.
- Open time, set time, pause/resume, transfer, remote lock/restart/shutdown, staff messages.
- Walk-in / Member / VIP pricing, member wallets, top-ups, billing history, dashboard charts.
- Seat map, wallpaper upload, automatic server detection, idle auto-shutdown.
- Point of sale, inventory, suppliers, sales report.
- Chat, activity monitoring (active program and window title only), member portal with optional Google sign-in.
- One-step installers, uninstaller, and release packaging.
