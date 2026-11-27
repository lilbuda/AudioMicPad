# Contributing to AudioMicPad

Use [GitHub Issues](https://github.com/lilbuda/AudioMicPad/issues) for bugs and feature requests. Search existing issues before opening a new one. For vulnerabilities, follow the [security policy](SECURITY.md).

## Build and test locally

Use 64-bit Windows and the .NET 8 SDK. AudioMicPad is a WPF desktop app. Install VB-CABLE separately to test virtual-microphone routing.

```powershell
dotnet restore --runtime win-x64
dotnet build --no-restore --configuration Release
```

The [README](README.md#building-from-source) covers publishing a self-contained executable and building the installer.

For audio changes, test the relevant paths: microphone and music through CABLE Output, and music monitoring through a headset if enabled. For playback changes, check play, pause, seek, and switching tracks. For UI changes, include a screenshot or recording when it helps explain the result.

## Submit changes

1. Fork the repository and create a branch from `main`.
2. Keep the change focused and use Conventional Commits, such as `fix(playback): preserve position when paused`.
3. Open a pull request against `main`. Describe the change, why it's needed, how you tested it, and any breaking changes.
4. Wait for the required Windows build and resolve review conversations before merging.

Keep generated binaries, local settings, secrets, signing material, and third-party installers out of the repository. Follow the [code of conduct](CODE_OF_CONDUCT.md) when participating.
