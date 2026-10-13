# Pattern Player

Video test-pattern player for LED processor testing with a Japanese control interface and a separate clean output window.

## Downloads

- [Apple Silicon Mac — v0.4.0](https://github.com/anm22884646/test-pattern-player/releases/download/v0.4.0/PatternPlayer-v0.4.0-mac-arm64.zip)
- [Intel Mac — v0.4.0](https://github.com/anm22884646/test-pattern-player/releases/download/v0.4.0/PatternPlayer-v0.4.0-mac-x64.zip)
- [Windows x64 — v0.3.0](https://github.com/anm22884646/test-pattern-player/releases/download/v0.3.0/PatternPlayer-v0.3.0-win-x64.zip)
- [Latest release page](https://github.com/anm22884646/test-pattern-player/releases/latest)

Mac: extract with macOS Archive Utility and move `Pattern Player.app` into Applications. macOS 12 or later. The Mac apps are ad-hoc signed and signature-verified, but not Developer ID signed or Apple-notarized. First launch may require System Settings > Privacy & Security > Open Anyway.

Windows: extract the complete folder and run `PatternPlayer.exe`. Keep the `vlc`, `TestPatterns`, and `Audio` folders together. Windows 10/11 with .NET Framework 4.8; no separate VLC installation is needed.

## Features

- Play, pause, Loop and timeline seeking.
- Output display selection and fullscreen without playback controls.
- FHD/UHD standard patterns and horizontal Motion Color Bars.
- Two players preload the loop transition to reduce blank frames.
- Audio Check with an independently looping test beat and HDMI/DisplayPort output selection. Audio follows play/pause; it does not seek with the video timeline.

## Validation and limits

The Apple Silicon macOS build passed native checks for all four video decoders, playback, pause, seek, loop switching and Audio Check. Both Mac packages passed code-signature verification. The Intel package has not yet been playback-tested on physical Intel hardware. Windows v0.3.0 passed compilation/startup checks.

Physical HDMI audio, multiple monitor behavior and 4K event performance require testing on your target computer/processor. No genlock or frame-perfect loop guarantee. The Mac version uses Electron/Chromium: H.264 MP4 is the primary format, and MOV support depends on its codec. ProRes is not validated.

## Source and builds

- `src/PatternPlayer`: Windows WPF/.NET Framework 4.8 source. Use `dotnet build -c Release`; copy media and VLC runtime from the release beside the EXE.
- `mac`: Mac Electron source. `python3 build-mac.py --arch both` on a Mac packages and ad-hoc signs both architectures. The GitHub Mac workflow restores media from the checksum-verified Windows release, builds the apps, runs native tests and publishes only on success.

VLC license notices are in the Windows package. Electron and Chromium license notices are included in the Mac packages.
