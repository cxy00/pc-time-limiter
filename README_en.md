# cmjLimit —— PC Daily Usage Time Limiter

[中文版 / Chinese version](./README.md)

A **Windows** parental-control / self-discipline tool that enforces a **daily allowed screen-time
quota per Windows account**. When the quota runs out it **automatically logs off the current user**,
and shows the remaining time in a persistent on-screen overlay.

> Written in C# / .NET 8 (WinForms). Relies only on the Windows API — no install and no third-party libraries.

---

## ✨ Features

- **Per-account quota**: Set a different daily limit for each Windows user in the config file
  (e.g. 1 hour for a child account, unlimited for a parent account).
- **Twin-process watchdog**: A Monitor process does the timing and logoff, while a Guard process
  instantly restarts it if killed — making it hard to shut down from Task Manager.
- **Persistent countdown HUD**: A corner overlay shows `Remaining 00:59`, always on top, click-through,
  and hidden from Alt+Tab / the taskbar.
- **Auto logoff at limit**: When time is up, a notice appears and, after a few seconds, the current
  user is logged off via `ExitWindowsEx`.
- **Early warning**: A popup warns the user when remaining time drops below a threshold, so they can save work.
- **Pause on lock**: Time does not accrue while the session is locked (configurable).
- **Relogin grace period**: If the daily quota is already spent, a later login gets a short grace window
  before being forced off.
- **Logon autostart**: Registered under `HKCU\...\Run` so it starts with the user session.
- **Hot-reload config**: Editing the config file takes effect within ~5 seconds, no restart needed.
- **Central management**: A shared config at `C:\ProgramData\cmjLimit\cmjLimit.ini` applies to all users.

---

## 📦 Build & Run

Requires the **.NET 8 SDK** (Windows) and **Windows 10/11**.

```powershell
# Clone, then enter the directory
cd cmjLimit

# Publish (Release, x64)
dotnet publish -c Release -r win-x64 --self-contained false

# Run
dotnet run -c Release
```

Build output lives in `bin/Release/net8.0-windows/`. Just launch `cmjLimit.exe`.

> Because it uses Windows Forms and Windows APIs, it builds and runs on Windows only.

---

## ⚙️ Configuration

On first run the program auto-creates a config file (in priority order):

1. `%LOCALAPPDATA%\cmjLimit\cmjLimit.ini` (per-user)
2. `C:\ProgramData\cmjLimit\cmjLimit.ini` (machine-wide / central)

Default content (`;` or `#` starts a comment):

```ini
; cmjLimit config file
[users]
; one account per line: username = daily allowed time
; time formats: 1h / 1h30m / 90m / 1.5h / 1:30 / 90 (bare number = minutes)
cmj=1h
lyy=1h30m

[sys]
; warn this many minutes before the limit
warn=5m
; grace period when logging in again after the daily quota is spent
grace=5m
; HUD position: TopRight / TopLeft / BottomRight / BottomLeft
position=TopRight
; overall opacity 0~1 (lower = more transparent)
opacity=0.82
; HUD font size (pt)
hudfont=13
; pause timing while the session is locked
pauselocked=true
```

Changes are reloaded automatically after ~5 seconds; adding/removing accounts under `[users]`
enables/disables monitoring for that account on the fly.

---

## 🔧 Command-line arguments

Append these to `cmjLimit.exe`:

| Argument       | Purpose                                                       |
| -------------- | ------------------------------------------------------------- |
| `--install`    | Register the app for the current user's logon autostart.      |
| `--uninstall`  | Remove the logon autostart entry.                             |
| `--stop`       | Write a stop flag; both processes exit within a few seconds.  |
| `--reset`      | Reset today's used time to zero (restart the timer).          |

Example:

```powershell
# Install & enable autostart
cmjLimit.exe --install

# Temporarily stop monitoring
cmjLimit.exe --stop

# Reset today's timer
cmjLimit.exe --reset
```

---

## 📁 Runtime data

Under `%LOCALAPPDATA%\cmjLimit\`:

- `cmjLimit.ini` —— configuration (see above).
- `usage.json` —— seconds used today (auto-zeroed across days).
- `cmjLimit.log` —— runtime log, useful for debugging watchdog / logoff behavior.

---

## ⚠️ Notes

- Auto logoff ends the current session; users should save their work in advance (a notice is shown first).
- The tool logs off rather than shutting down / restarting, so system data is unaffected.
- The watchdog makes stopping harder. To fully disable: run `--stop`, then `--uninstall`, or remove the
  corresponding registry entry.
- This program is intended for reasonable time management and parental supervision only. **Do not use it
  to circumvent limits placed on others without their informed consent.**

---

## 📄 License

Released under the MIT License — see [LICENSE](./LICENSE).
