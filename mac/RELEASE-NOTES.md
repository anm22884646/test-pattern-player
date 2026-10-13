# First macOS release

- `mac-arm64.zip`: Apple Silicon (M1 and later).
- `mac-x64.zip`: Intel Mac.
- Japanese control window, separate clean output, display selection and fullscreen.
- Built-in FHD/UHD patterns, Motion Color Bars, play/pause/seek and dual-player Loop.
- Audio Check with the supplied testing beat and selectable HDMI/DisplayPort audio output.

Extract using macOS Archive Utility and move `Pattern Player.app` into Applications. macOS 12 or later is required. Apps are ad-hoc signed and signature-verified, but are not Apple Developer ID signed or notarized. You may need System Settings > Privacy & Security > Open Anyway for first launch.

The native Mac smoke test checks built-in video decoding, playback, pause, seeking, loop switching and Audio Check. Actual physical HDMI audio, monitor switching and 4K event performance still require testing on the target Mac. MOV support depends on its codec; H.264 MP4 is recommended. ProRes is not validated.

Windows users should use the [v0.3.0 Windows release](https://github.com/anm22884646/test-pattern-player/releases/tag/v0.3.0).
