# Changelog

All notable changes to this project are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added
- **Opus music mode (major, uncoloured centre).** The voice encoder now requests the Opus *audio* application instead of *voip*. VOIP is a speech path: on music it folds the stereo centre (vocals and bass) down while the wide/reverb content survives, so a centred mix arrives thin and distant. In music mode the full stereo image is coded. Native patch in `discord_voice.node`, at the same encoder-config builder as the stereo patch.
- **No mid-call mono downgrade (keeps stereo steady).** Discord's audio network adaptor commits a single channel whenever its uplink estimate dips, and the codec then folds L/R to (L+R)/2 — audio that intermittently collapses toward mono during a call. The runtime channel downgrade is now skipped, so two channels are held for the whole call. Safer than pinning the value, which would fatal-assert if the encoder ever held fewer channels than requested.

### Fixed
- **Stereo flag and bitrate are now enforced on every connection (major).** The hook only upgraded the encoder when it arrived as mono; when Discord already reported 2 channels (the native patch sets that), it left the connection alone — so the Opus `stereo` flag stayed 0 and the bitrate stayed at Discord's ~64 kbps default. Verified in the WebRTC logs: `ConfigureStream ... stereo=0` and `rate=64000`. The hook now forces `channels=2`, `stereo=1` and the chosen bitrate on every `setTransportOptions`, so the connection reliably reports `stereo=1` at the configured bitrate.
- **`--verify` no longer reports stale readings.** The WebRTC logs persist across sessions, so verify could show channel/processing values from an old voice session as if they were current. It now checks the log's most recent timestamp and, if there was no voice activity in the last 5 minutes, asks you to rejoin voice instead of printing outdated numbers.

### Changed
- **`--verify` reads the stream config from `discord-last-webrtc` and reports whether the APM is actually running.** Stream setup (`ConfigureStream` / `ApplyConfig`) is logged to `discord-last-webrtc`, not the live `discord-webrtc`, so verify now reads both; the verdict is based on the fresh captured channel count, and it shows the live `APM frames processed` count so configured-but-idle processing is not mistaken for active processing.
- Corrected the note about AGC2. Controlled testing does not support the earlier claim that AGC2 was responsible for the altered dynamics on music; on/off made no audible difference, and the received phase problem reproduces independently of it. See the investigation in the issue tracker.

## [1.2.0]

### Added
- **Processing diagnostic in `--verify` / menu "Verify".** It now reads Discord's own logs and reports each capture processing stage — high-pass (bass), echo cancel, noise suppression, AGC1 and AGC2 — so you can see what the capture pipeline is configured to do. High-pass is reported from the actual patch state (its config flag stays 1 even when the patched function is neutralized). Join a voice channel first for a fresh reading.

## [1.1.0]

### Added
- **English/Turkish UI.** Language auto-detected from the OS, overridable with `--lang en|tr` and switchable from the menu. Choice is remembered.
- Settings file (`%LOCALAPPDATA%\DiscordStereo\settings.txt`) that remembers language and bitrate.
- Bilingual documentation: English `README.md` + Turkish `README.tr.md` with a language switcher.
- Troubleshooting / FAQ, including an honest note about Discord's server-side bitrate cap.
- `SECURITY.md`, `CHANGELOG.md`, and a `.csproj` for `dotnet build`.
- GitHub Actions CI that builds the exe from source and publishes it with a SHA-256 checksum on tagged releases.

### Changed
- **Graceful partial patching:** if a native signature is missing (e.g. a new Discord version changed that code), the tool now warns and applies the remaining sites instead of aborting everything. It still refuses to write anything only when *no* signature matches.
- Wording clarified so "Opus" is unambiguously the audio codec.

## [1.0.0]

### Added
- Initial release. Signature-based patches to Discord's `discord_voice` module for true stereo through the mic channel:
  - Stereo (channels 1→2), downmix bypass, high-pass filter off.
- JS hook that sets the encoder to 2 channels at a high bitrate (up to 510 kbps) and disables mono-collapsing processing, with low latency (FEC off).
- Single self-contained exe: menu, `--apply` / `--restore` / `--status`, optional auto-start.
- Automatic backups with byte-for-byte restore. Version-independent detection of Stable/PTB/Canary/Development.
