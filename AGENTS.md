# Gwendoline for Windows

Gwendoline is a Windows tray app that keeps a laptop awake with the lid closed and
optionally sends Telegram notifications. A port of gamween/pauline (macOS).
Keep safety rules in Gwendoline.Core and system integration in Gwendoline.

## Validation

- `dotnet test` runs the Gwendoline.Core xUnit suites.
- `./build.ps1` publishes build\Gwendoline.exe. `dotnet build` checks types.
- No separate lint tool is configured. Check `git diff --check` before committing.

## Operational constraints

- Tests must not change real power plans or use personal Telegram data.
- `install.ps1` replaces the app, adds a Run entry and a Start menu shortcut. No admin.
  Building alone does not install anything.
- The lid setting survives reboots. state.json is written before any power plan
  changes, and every launch restores from it.
- All app state lives on the UI thread; power changes are synchronous calls.
- Keep the bot token out of logs, exception texts and command arguments.
