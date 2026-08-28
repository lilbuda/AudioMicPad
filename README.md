# AudioMicPad

AudioMicPad is a small, open-source Windows soundboard and audio mixer for this workflow:

**physical microphone + music/soundboard → VB-CABLE → game voice chat**

It also lets you monitor music and sound effects through your normal headset without routing your voice back to yourself.

**[Visit the official AudioMicPad website](https://audiomicpad.vercel.app/)** for downloads, release notes, and the complete setup guide.

## Features

- Mixes a physical microphone with music or sound effects and sends the result to **CABLE Input**.
- Sends music and sound effects—but not microphone audio—to a selected headset or speaker.
- Independent voice, virtual-microphone music, and headset-monitor music volume controls, with up to 500% voice gain for quiet microphones.
- Supports WAV, MP3, M4A, AAC, WMA, FLAC, and OGG files.
- Play, pause, stop, previous, next, loop-song, and loop-playlist controls.
- Assignable global shortcuts for sounds and playback controls.
- System, dark, and light themes.
- Built-in usage and routing guide.
- Automatically saves the folder, selected devices, volumes, theme, shortcuts, and engine options.

## Requirements

- Windows 10 or Windows 11, 64-bit.
- The standard/free [VB-CABLE virtual audio driver](https://vb-audio.com/Cable/).

The packaged application is self-contained, so users do not need to install .NET separately.

## Download

Download the latest Windows installer from the [AudioMicPad v1.1.1 release](https://github.com/lilbuda/AudioMicPad/releases/download/v1.1.1/AudioMicPad-Setup-v1.1.1.exe).

Because the installer is not currently code-signed, Windows may identify its publisher as unknown or show a Microsoft Defender SmartScreen warning. Only download AudioMicPad from this repository's official Releases page.

## Audio routing

VB-CABLE's **CABLE Input** is its Windows playback/output endpoint. **CABLE Output** is its Windows recording/input endpoint. Audio sent to CABLE Input is forwarded to CABLE Output.

In AudioMicPad:

1. Set **Microphone** to your physical microphone.
2. Set **Game output** to `CABLE Input (VB-Audio Virtual Cable)`.
3. Set **Headset / speaker monitor** to your normal headset or speakers.
4. Enable headset monitoring if you want to hear music and sound effects locally.
5. Press **Start / restart audio**.

In Discord, a game, or another receiving application, select `CABLE Output (VB-Audio Virtual Cable)` as the microphone/input. Do not select CABLE Input as the microphone.

## Building from source

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then run this from the repository root in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The self-contained application is created at `publish\AudioMicPad.exe`.

### Building the installer

Install [Inno Setup](https://jrsoftware.org/isdl.php), then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
```

The installer is created at `installer\AudioMicPad-Setup-v1.1.3.exe`. It requests administrator permission because Program Files is protected. If VB-CABLE is not detected, the installer offers to open its official download page; the VB-CABLE driver is not bundled.

## Privacy

AudioMicPad processes audio locally and does not collect analytics or transmit audio. Settings are stored locally in `%APPDATA%\AudioMicPad\settings.json`.

## Contributing

Issues and pull requests are welcome. Please keep changes focused and describe how they were tested. Do not commit generated binaries, local settings, certificates, private keys, or other secrets.

## Third-party software

AudioMicPad uses [NAudio](https://github.com/naudio/NAudio), which is distributed under the MIT License. VB-CABLE is separate donationware published by VB-Audio Software and is not part of this repository. See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) for details.

## License

AudioMicPad is licensed under the [MIT License](LICENSE).
