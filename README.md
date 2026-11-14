# AI Usage Monitor (`agy_app_AI_usage`)

Minimalist Windows 11 taskbar widget for monitoring **Antigravity** and **Claude Code** usage limits in real time.

## Features
- **Windows 11 Taskbar Integration:** Borderless `12px` `Segoe UI Variable Text` layout matched to the native Windows 11 clock, anchored to the bottom-left of the taskbar.
- **Antigravity (`AG` / `Ar`):** Reads live quota fractions and reset timers from the local Antigravity `language_server` (`Hydrogen (5h)` and `Argon (Weekly)`).
- **Claude Code (`CL` / `W`):** Reads local OAuth credentials (`~/.claude/.credentials.json`) and fetches 5-hour session and 7-day weekly limits.
- **Detail Flyout:** Left-click the widget to open a dark Win 11 card with per-model progress bars and reset countdowns.
- **Right-Click Menu:**
  - Refresh now / Open detail
  - Toggle individual providers (`AG` / `CL`)
  - Configurable refresh interval (`30s`, `1m`, `2m`, `5m`)
  - Toggle **Start with Windows** (`HKCU\...\Run`)

## Requirements
- Windows 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- Antigravity Desktop (running locally) and/or Claude Code CLI (`claude auth login`)

## Build
```powershell
dotnet build AiUsageMonitor.csproj -c Release
```
