# Gwendoline for Windows

A Windows tray app that keeps a laptop awake with the lid closed. A port of [Pauline for macOS](https://github.com/gamween/pauline), renamed Gwendoline.

Turn it on, close the lid, and your builds, downloads and coding agents keep running. Turn it off and the PC sleeps normally again.

Gwendoline can also message you through your own Telegram bot: when it turns on, when the battery runs low and when it turns off. You can turn it off from your phone.

## Requirements

- Windows 10 or 11 on a laptop
- The .NET 9 SDK or later (`winget install Microsoft.DotNet.SDK.9`)

No administrator account: Windows lets any user choose what closing the lid does.

## Install

```powershell
git clone <this repository> gwendoline-windows
cd gwendoline-windows
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

The script builds the app from source, copies it to `%LOCALAPPDATA%\Programs\Gwendoline`, adds it to the programs that start at login and to the Start menu, then starts it. Gwendoline appears in the taskbar, next to the clock. The script also asks Windows 11 to show the icon on the taskbar rather than behind the arrow.

Keep the cloned folder. To update, run `git pull`, then `install.ps1` again.

## Use

| Action | Result |
| --- | --- |
| Click the icon | Turn Gwendoline on or off |
| Right-click | Menu: state, battery, **Stay Awake** or **Allow Sleep**, Telegram, **Quit Gwendoline** |

The icon is the emoji of a woman getting a massage while Gwendoline is off and the PC sleeps normally, and a woman at her laptop while Gwendoline is on and the PC stays awake, even with the lid closed. Gwendoline draws them at launch from the Segoe UI Emoji font, in one color that follows a light or dark taskbar.

Quitting turns Gwendoline off. To start it again, open it from the Start menu, or log in again.

## How it works

Each power plan has a setting for what closing the lid does. Turning Gwendoline on saves that setting for every plan to `%LOCALAPPDATA%\Gwendoline\state.json`, then sets them all to **Do nothing**. Gwendoline changes every plan, not just the active one, so the PC stays awake when the plan changes, for example when the laptop maker's app switches performance modes. A plan that appears while Gwendoline is on gets **Do nothing** too. While Gwendoline is on, it also blocks idle sleep. Turning Gwendoline off puts every plan back the way it was.

Every 5 seconds, Gwendoline reads its state, the lid, the battery and the displays. If you change what closing the lid does in Windows settings while Gwendoline is on, your choice wins: Gwendoline puts the other plans back and turns off.

## Safety

The lid setting survives a restart. Windows keeps its own critical battery action and thermal protection the whole time. On top of that:

| Case | What Gwendoline does |
| --- | --- |
| Battery at or under 5% and not charging | Turns off. A closed PC without an external display goes to sleep. |
| You try to turn it on in that case | Refuses, with an alert that says why. |
| Quit, log out, restart, shut down | Gives sleep back before it exits. |
| Gwendoline crashes | Its supervisor starts it again within seconds. Every launch starts off. After 5 crashes in a minute, the supervisor gives sleep back and stops. |
| The PC restarts | `state.json` survives, so Gwendoline gives sleep back when it starts at login. |
| External display connected | Never puts the PC to sleep. |

A closed laptop under heavy load still gets hot. Leave it on a desk, not in a bag.

To change the battery floor (it applies within 5 seconds):

```powershell
reg add HKCU\Software\Gwendoline /v BatteryFloor /t REG_DWORD /d 10 /f   # 0 turns the floor off
reg delete HKCU\Software\Gwendoline /v BatteryFloor /f                   # back to 5%
```

## Telegram

Optional. Gwendoline talks to your own bot through the Telegram Bot API, with long polling from your PC. There is no server in between.

### Connect

1. Right-click the icon, then **Connect Telegram…**
2. Click **Open BotFather**, send `/newbot` and follow the steps. Use a new bot for each PC: two PCs cannot read the same bot.
3. Paste the token BotFather gives you and click **Connect**.
4. Telegram opens the chat with your bot. Tap **Start**. If Telegram is only on your phone, copy the link Gwendoline shows and open it there. **Open the Bot Chat…** in the menu brings the link back until you tap Start.

The bot answers `Gwendoline is connected.` with its commands. The Start link carries a one-time code, so only your chat gets linked. After that, the bot ignores every other chat.

### Messages

Each message gives the state, then the battery:

```
Gwendoline is on
Battery 94%, 20 h left
```

| When | First line |
| --- | --- |
| Gwendoline turns on | `Gwendoline is on`, with a **Turn Gwendoline off** button |
| The battery drops to 30%, 20% and 10% while not charging | `Gwendoline is on`, with the same button |
| Charging ends: full, or held at the laptop's charge limit | `Gwendoline is on` |
| Gwendoline turns off, whatever the cause | `Gwendoline is off`, with a reason in some cases |

| Reason | Cause |
| --- | --- |
| `(battery low)` | The battery floor was reached |
| `(logged out)`, `(PC shut down)` | You logged out, or shut down or restarted |
| `(Gwendoline crashed)` | Gwendoline crashed. Sent when its supervisor starts it again. |
| `(PC restarted)` | Power loss, system crash or forced restart. Sent at the next login. |

The closing message is always the last one of a session. Gwendoline saves the session to disk before the opening message goes out, so a crash, a restart or a network outage only delays the closing message. Reminders and replies that cannot leave within 2 minutes are dropped. A closed PC waits at most 10 seconds for the closing message before going to sleep.

### Commands

| Command | Answer |
| --- | --- |
| `/status` | `Gwendoline is on` or `Gwendoline is off`, then the battery |
| `/off` | Turns Gwendoline off and sends the closing message. A closed PC without an external display goes to sleep. It never shuts down. |

The token, the chat ID and the open session are stored in `%LOCALAPPDATA%\Gwendoline\telegram.json`. The token is encrypted by Windows for your user on this PC. **Disconnect Telegram** in the menu deletes the file.

## Claude Code

Optional. While Gwendoline is on, it tells you through the bot when Claude Code has finished, and lets you answer Claude Code's permission requests from your phone. Two hooks in `~/.claude/settings.json` run `Gwendoline.exe --claude`:

```json
"Stop": [{ "hooks": [{ "type": "command", "command": "C:/Users/<you>/AppData/Local/Programs/Gwendoline/Gwendoline.exe", "args": ["--claude"], "timeout": 30 }] }],
"PermissionRequest": [{ "hooks": [{ "type": "command", "command": "C:/Users/<you>/AppData/Local/Programs/Gwendoline/Gwendoline.exe", "args": ["--claude"], "timeout": 3600 }] }]
```

| When | What the bot does |
| --- | --- |
| Claude has finished | Sends a rude line picked at random, then the project folder |
| Claude asks for a permission | Sends the tool and the command, with **✅ Allow** and **❌ Deny** buttons. `oui`, `ok`, `allow` or `non`, `deny` typed in the chat work too, when one request waits. |

Whether Gwendoline is on is decided when the hook runs: turn it on during a long prompt and you still hear when it ends. While Gwendoline is off, the hooks return at once and Claude Code asks in the terminal as usual. A request still waits in the terminal when nobody answers within 55 minutes or when Gwendoline turns off. The lines are in `src/Gwendoline.Core/ClaudeText.cs`.

Only the app reads the bot's messages, since Telegram allows one reader per bot, so the hook talks to it through a named pipe open to your user only.

## Uninstall

From the cloned folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

It stops Gwendoline, which gives sleep back and sends its closing message. Then it removes the app, the login entry, the Start menu shortcut, the settings and the Telegram file.

## Differences from Pauline for macOS

- No `sudo` and no sudoers rule: changing the lid action needs no administrator rights on Windows.
- No thermal check: Windows has no such signal for apps, and it keeps its own thermal protection while Gwendoline is on.
- No "full in" time while charging: Windows does not give one.
- The Start link is shown as text to copy, not as a QR code.
- Windows does not tell a shutdown from a restart: both say `(PC shut down)`.

## Development

```powershell
.\build.ps1    # release build in build\Gwendoline.exe
dotnet test    # Gwendoline.Core tests
```

`Gwendoline.exe` with no argument is the supervisor: it runs `Gwendoline.exe --app`, the tray app, and starts it again after a crash. `Gwendoline.exe --quit` asks a running Gwendoline to quit cleanly. `Gwendoline.exe --restore` gives the lid actions back when Gwendoline is not running. `Gwendoline.exe --claude` is the Claude Code hook.

```
src/Gwendoline.Core/      rules and texts in plain C#, no system calls, unit tested
  Policy.cs            power state actions, battery floor, what to do at launch and on each check
  Notifier.cs          when to send a battery reminder or the charging message
  TelegramText.cs      every message the bot sends
  StatusText.cs        menu, tooltip and refusal alert texts
  ClaudeText.cs        the bot's lines about Claude Code, and reading oui / non
src/Gwendoline/           the WinForms app: reads the system, changes the lid action, talks to Telegram
  Program.cs           entry point, supervisor, one copy at a time
  TrayApp.cs           icon, menu, checks every 5 seconds, quitting, Telegram setup
  LidSleep.cs          the lid action of every power plan, saved and restored
  SystemReadings.cs    battery, displays, idle sleep, sleep now
  PowerWindow.cs       hidden window for the lid, wake and session end
  Telegram.cs          Bot API client, long polling, ordered outbox, sessions on disk
  TelegramDialogs.cs   token and Start link windows
  EmojiIcon.cs         the two emoji as monochrome tray icons
  ClaudeBridge.cs      the Claude Code hook and the named pipe it uses
tests/                 xUnit suites
install.ps1            app, login entry, Start menu shortcut
uninstall.ps1          removes all of it
```

## License

MIT. See [LICENSE](LICENSE).
