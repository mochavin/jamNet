# JamNet

A tiny Windows widget that shows a **clock (with seconds)** and your **real-time internet speed**. It stays on top of other windows, has a minimal look, and you can drag it anywhere.

![Clock + network](screenshots/jam-network.png)

| Clock only | Network only |
|---|---|
| ![Clock only](screenshots/jam.png) | ![Network only](screenshots/network.png) |

## Features

- Stays on top of other windows, even while another app has focus
- 24-hour clock (`HH:mm:ss`), updated every second
- Current download (↓) and upload (↑) speed, with short units (`B`, `K`, `M`, `G` per second)
- Three display modes: clock + network, clock only, or network only
- Per-pixel semi-transparent background: only the background is see-through, the text stays sharp
- Hidden from the taskbar and Alt+Tab
- Position, size, transparency, and mode are saved automatically
- Optional start with Windows
- A single `.exe` of about 20 KB, with no installation and no extra dependencies

## Running

Double-click `JamNet.exe`. The widget appears in the top-right corner of the screen.

Only one copy can run at a time. Opening it again while it's already running does nothing.

## Usage

- **Left-click and drag** to move the widget
- **Right-click** to open the menu: display mode, always on top, start with Windows, move to corner, hotkeys, exit

The menu and help text are in Indonesian:

| Menu item | Meaning |
|---|---|
| Tampilan → Jam + network / Jam saja / Network saja | Display → Clock + network / Clock only / Network only |
| Selalu di atas | Always on top |
| Jalankan saat Windows start | Start with Windows |
| Pindah ke pojok kanan atas | Move to top-right corner |
| Hotkey... | Show hotkeys |
| Keluar | Exit |

### Hotkeys

Click the widget first so it has focus (a blue outline appears), then:

| Key | Action |
|---|---|
| Arrow keys | Move 1 px |
| Shift + Arrow keys | Move 10 px |
| `+` / `-` or scroll | Background more opaque / more transparent |
| Ctrl + `+` / `-` or Ctrl + scroll | Bigger / smaller |
| `0` | Reset size and transparency |
| `M` | Switch display mode: clock + network → clock → network |
| `T` | Turn always on top on/off |
| `R` | Move to the top-right corner |
| `F1` / `H` | Show the hotkey list |
| Ctrl + Q | Exit |

## Building from source

You don't need Visual Studio or the .NET SDK. Windows already ships a C# compiler (.NET Framework 4):

```bat
build.bat
```

This creates `JamNet.exe` in the same folder. Close the app before rebuilding, because a running `.exe` can't be overwritten.

## Files

| File | Contents |
|---|---|
| `JamNet.cs` | All of the source code (C# WinForms + Win32) |
| `build.bat` | Build script |
| `JamNet.exe` | Built app |
| `screenshots/` | Images for this README |

Settings are saved in `%APPDATA%\JamNet\settings.ini`. Delete this file to go back to the defaults.

## Notes

- The speed shown is the total of all active network adapters (except loopback and tunnel adapters). If you use a VPN or a virtual adapter such as Hyper-V or WSL, the same traffic may be counted twice.
- The "start with Windows" option writes to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. If you move the `.exe`, turn the option off and on again.
