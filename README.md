# Pattern Player

Windows test-pattern video player for LED processor testing, with a Japanese control interface and a separate clean fullscreen output window.

## Download

[Download the latest release](https://github.com/anm22884646/test-pattern-player/releases/latest)

Select the `PatternPlayer-*-win-x64.zip` asset. Extract the complete folder and run `PatternPlayer.exe`. Keep the bundled `vlc`, `TestPatterns`, and `Audio` folders together. Windows 10/11 with .NET Framework 4.8 is required; a separate VLC installation is not required.

## Features

- Play, pause, loop, and timeline seeking.
- Display selection and fullscreen output without playback controls.
- FHD/UHD standard test patterns and horizontal Motion Color Bars.
- Double-buffered video looping.
- Audio Check with a bundled test beat, independent audio looping, and selectable HDMI/DisplayPort output.

Audio follows video playback and pause, but does not seek with the video timeline. Choose the correct HDMI/DisplayPort endpoint in the audio selector; the connected processor must support audio.

## Validation

v0.3.0 passed compilation and startup checks. Verify HDMI audio routing and video performance on the actual playback computer and processor. ProRes is not currently validated.

## Build from source

The WPF application targets .NET Framework 4.8. Run `dotnet build -c Release` in the source directory. To run, place the bundled VLC runtime and the `TestPatterns` and `Audio` assets from the release package beside the executable.

VLC license documents are included in the release's `vlc` folder.
