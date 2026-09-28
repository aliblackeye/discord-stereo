# Changelog

All notable changes to this project are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [1.3.1] - 2026-09-28

### Fixed
- **The periodic ~once-a-minute dropout on a continuous stream is gone.** Sending audio non-stop (mic never idling), the far end heard the sound cut out and return roughly every 60 seconds. It was not the network — the WebRTC logs show the bandwidth estimate pinned high, RTT low and zero outbound loss throughout. The cause is Discord's UDP socket rebinding to a fresh local port every 60 seconds as a proactive path refresh (`udp_socket.cpp`, "Reconnection started, sending echo"); the brief gap during that rebind only became audible once v1.3.0 turned FEC off on a continuous high-bitrate stream. Continuous audio already keeps the NAT mapping alive, so the rebind is redundant while streaming, and a genuine path failure is still handled by the higher-level RTC reconnect. A native patch raises the socket's reconnect interval from 60 seconds to effectively never for a session (24 h), so the stream is no longer interrupted. It does not touch the audio path, so the v1.3.0 centre fix is unchanged.

## [1.3.0] - 2026-09-27

### Fixed
- **The received centre no longer thins out — a stray packet-loss assumption was collapsing it (the headline fix).** On a stereo *music* mix the phantom centre (lead vocal, bass, kick) reached the far end weak and unstable a fraction of a second into the call, while the wide reverb tails came through fine — the received signal showed up strongly out of phase. Root cause: the Opus encoder was being driven as if the link had heavy packet loss. The live stream config carried a 30% minimum packet-loss figure with in-band FEC on, and libopus answers that by coding defensively — it widens the CELT stereo spread and spends part of the bitrate on redundancy instead of the signal, which is exactly what hollows out a centred image. The encoder now runs at 0% packet loss with FEC off, so the full bitrate goes to the signal and the centre stays put. Native patches in `discord_voice.node` set the packet-loss rate handed to libopus to 0 and force both FEC-enable sites off; the JS hook also clears the FEC flag on the transport options.
- **Stereo flag and bitrate are now enforced on every connection.** The hook previously only upgraded the encoder when it arrived as mono; when Discord already reported 2 channels (the native patch sets that), the Opus `stereo` flag stayed 0 and the bitrate stayed at Discord's ~64 kbps default (`ConfigureStream ... stereo=0`, `rate=64000` in the WebRTC logs). It now forces `channels=2`, `stereo=1` and the chosen bitrate on every `setTransportOptions`.
- **`--verify` no longer reports stale readings.** The WebRTC logs persist across sessions, so verify could show channel/processing values from an old voice session as if they were current. It now gates on the log's most recent timestamp and, if there was no voice activity in the last 5 minutes, asks you to rejoin voice instead of printing outdated numbers.

### Added
- **No mid-call mono downgrade (keeps stereo steady).** Discord's audio network adaptor commits a single channel whenever its uplink estimate dips, and the codec then folds L/R to (L+R)/2 — audio that intermittently collapses toward mono during a call. The runtime channel downgrade is now skipped, so two channels are held for the whole call. Safer than pinning the value, which would fatal-assert if the encoder ever held fewer channels than requested.
- **Opus music application selected explicitly.** The encoder config requests the Opus *audio* application (music) rather than the speech-tuned *voip* path. On the current build a stereo stream already selects it, so this is belt-and-suspenders against a build or code path that would otherwise fall back to voip.

### Changed
- **`--verify` reads the stream config from `discord-last-webrtc` and reports whether the APM is actually running.** Stream setup (`ConfigureStream` / `ApplyConfig`) is logged to `discord-last-webrtc`, not the live `discord-webrtc`, so verify now reads both; the verdict is based on the fresh captured channel count, and it shows the live `APM frames processed` count so configured-but-idle processing is not mistaken for active processing.
- Corrected the note about AGC2. Controlled on/off testing showed no audible difference on music, and the received-phase problem reproduces independently of it, so AGC2 is not responsible for the altered dynamics claimed earlier.

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
