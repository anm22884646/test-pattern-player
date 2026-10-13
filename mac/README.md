# Pattern Player for macOS — v0.4.0

Separate Japanese control and clean video-output windows for LED processor testing.

## Install

Download `mac-arm64` for Apple Silicon (M-series), or `mac-x64` for an Intel Mac. Extract the ZIP using macOS Archive Utility and drag `Pattern Player.app` into Applications. Run the complete .app; no VLC installation is needed. macOS 12 or later is required by the Electron runtime.

This initial Mac build is ad-hoc signed when built on a Mac, but is not Apple Developer ID signed or notarized. macOS may require approval through System Settings > Privacy & Security > Open Anyway after you attempt to launch it. Do not use the unsigned Windows-produced staging ZIP as a final distribution; use the Mac-built release instead.

## Use

1. Open a built-in FHD/UHD standard pattern or Motion Color Bars, or select a local video.
2. Choose the output display and click 全画面. The control window remains on the primary display. When only the primary display exists, controls stay above the output.
3. Use 再生, 一時停止, Loop and the timeline. Space toggles playback, F11 toggles fullscreen, and Esc exits fullscreen.
4. Enable Audio Check and select the HDMI/DisplayPort output. Audio follows video play/pause and loops independently. Video seeking does not seek the audio. If output selection is unavailable, select HDMI in macOS System Settings > Sound > Output. No microphone recording is used.
5. Refresh audio outputs after reconnecting a cable. A disconnected video display pauses playback and returns output to the primary display.

## Media support and limits

The Mac version uses Electron/Chromium media decoding, rather than the Windows VLC backend. H.264/AAC MP4 is the primary target; MOV support depends on the codec contained in the file. ProRes is not currently supported or validated. The supplied test beat MP3 and all four test-pattern MP4 files are bundled in the application.

Two video elements preload the loop transition, retaining the visible frame until the next player has decoded a frame. This reduces blank transitions; frame-perfect, genlocked or zero-drop playback is not guaranteed. Verify actual 4K performance, HDMI audio and monitor behavior on your event computer.

## Build

On a Mac with Python 3.11 or later: `python3 build-mac.py --arch both`. The script downloads official Electron 44.7.0 archives, verifies upstream SHA-256, preserves framework symlinks, ad-hoc signs and verifies each .app, then produces architecture-specific ZIPs. All bundled assets must be present in TestPatterns and Audio.

Electron's license and Chromium third-party license notices are distributed with the application. The original Windows WPF version remains available separately.
