# Changelog

All notable changes to this project are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/).

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
