# SoundSleuth

SoundSleuth is a Windows tool that finds out which program just made a sound. It watches every audio output, lists each sound with the program that played it, saves a short replay clip, and lets you mute or end that program with one click. It is a single `.exe` of about 50 KB that runs without installation or administrator rights, and it is licensed under MIT.

![Release](https://img.shields.io/github/v/release/mhsutton07/SoundSleuth)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)
![Windows 10](https://img.shields.io/badge/platform-Windows%2010-informational)

## Install

Download `SoundSleuth.exe` from the [latest release](https://github.com/mhsutton07/SoundSleuth/releases/latest) and run it. There is no installer and nothing to configure.

The exe is not code-signed, so Windows SmartScreen may show "Windows protected your PC" the first time:

1. Click **More info**.
2. Click **Run anyway**.

To check that your download is the published file, compare its hash with the SHA256 in the release notes:

```
certutil -hashfile SoundSleuth.exe SHA256
```

Prefer to build it yourself? See [Build from source](#build-from-source).

## Usage

Run `SoundSleuth.exe`. The window lists every sound as it plays, newest first:

| Column | Meaning |
| --- | --- |
| Time | When the sound started |
| App, PID, Path | The program that played it |
| Device | The audio output it played on |
| Peak | The loudest level reached (0.000 to 1.000) |
| Duration | How long it played |
| Clip | The saved replay clip |
| State | Playing, Ended, or Muted (still playing) |

Select a row to use the buttons:

- **Play clip** plays the saved replay of that sound.
- **Mute** / **Unmute** switches that program's audio session off or on. A muted program is still listed while it keeps playing.
- **Kill process** ends the program after a Yes/No confirmation that names the exact process and PID. It is disabled for Windows System Sounds.

The **Toasts** checkbox turns the first-sound notification on or off for the current run. The status line at the bottom reports log errors and notifications that Windows suppressed.

### Console mode

```
SoundSleuth.exe --console 60
```

Listens for 60 seconds, prints one line for each finished sound, and exits with code 0. Any other arguments print a usage message and exit with code 2. This mode does not need a window, so it works from scripts.

```
2026-10-14 21:03:44.554 | powershell | pid 56512 | C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe | Speakers (Realtek(R) Audio) | peak 0.071 | 3.0 s | hint: - | clip: 20261014-210344_powershell_56512.wav
```

### Where the files go

| What | Location |
| --- | --- |
| Event log, one line per sound | `%LOCALAPPDATA%\SoundSleuth\sounds.log` |
| Replay clips (WAV) | `%LOCALAPPDATA%\SoundSleuth\clips\` |

The clips folder is capped at 500 MB and 500 files: after each new clip, the oldest clips are deleted. `sounds.log` itself is small text and is never trimmed.

If the log cannot be written, the window and console mode show the error and keep running.

## How it works

SoundSleuth polls the Windows Core Audio session meters of every active output device 10 times a second. A session counts as playing when its peak level is above 0.01, and a sound ends after one second below that. Each sound is attributed to the process that owns the session. In parallel it keeps the last 25 seconds of each device's output in memory using WASAPI loopback, and cuts a clip (from 0.5 s before the sound to its end, at most 20 s) when the sound ends. It uses no drivers, no hooks, and no network access.

## Limits

- **Only software that plays through Windows' audio engine is visible.** Hardware beeps, driver or firmware sounds, and apps using exclusive-mode audio do not appear.
- **Beeps show up as System Sounds.** `Beep` and `MessageBeep` are played by Windows itself, so they are listed as "Windows System Sounds" with a hint naming the foreground app at that moment. The hint is a guess, not proof.
- **Browsers are reported per app, not per tab.** Chromium-based browsers and Electron apps play audio from a helper process. SoundSleuth shows it as `<app> (browser audio)`. Kill ends the whole app, because ending only the helper makes it restart.
- **Clips are the whole device mix.** If two programs play at once, both are in the clip.
- **Tested on Windows 10.** Windows 11 uses the same audio APIs and should work, but has not been tested yet.
- **Processes you cannot open show "(access denied)"** as their path, for example elevated programs.
- **Windows may hide the notification.** If notifications are off, the taskbar button flashes instead and the status line says so. To turn them on, open Settings > System > Notifications, enable "Get notifications from apps and other senders", and turn off Focus assist if it is active.

## Build from source

You need 64-bit Windows with the .NET Framework 4.x, which provides the `csc.exe` compiler. No SDK, NuGet packages, or Visual Studio are required.

```
build.cmd
```

The result is `bin\SoundSleuth.exe`. The code is C# 5, WinForms, and direct Core Audio COM interop in `src\`.

Tests are in `tests\`. `verify_console.sh` (run from Git Bash) plays short quiet tones from its own PowerShell process and checks the console output, log, and clips. `build_probe.cmd` builds `MuteProbe.exe`, which mutes and unmutes that tone's session.

## Non-goals

- Identifying what a sound is (no fingerprinting or speech recognition).
- Recording only one program's audio.
- Capturing exclusive-mode streams.
- An installer, auto-update, telemetry, or any network feature.

## Project status

Version 1.0. Settings are not saved between runs.

## Contributing

Issues and pull requests are welcome on GitHub.

## Security

Please report vulnerabilities privately through the repository's Security tab on GitHub rather than in a public issue.

## License

MIT. See [LICENSE](LICENSE).
