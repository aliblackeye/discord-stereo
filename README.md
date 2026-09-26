# Discord Stereo

**🇬🇧 English** · [🇹🇷 Türkçe](README.tr.md)

Send **true stereo** audio to Discord at a **high bitrate** (up to 510 kbps, the maximum the Opus audio codec supports) with low latency — straight through the **microphone channel**, no Go Live / screen share needed. Perfect for listening to music together, playing an instrument, or sharing any stereo source so the other side hears **left/right separately instead of mono**.

Discord downmixes the microphone path to a single channel (mono) to save bandwidth. This tool applies small, **signature-based** patches to Discord's `discord_voice` module that stop the downmix and set the encoder to 2 channels at a high bitrate. Because the patches are signature-based, the tool is **version-independent**.

> ⚠️ This tool modifies **your own Discord client on your own computer**. Client modifications may violate Discord's Terms of Service; use at your own risk. Everything runs locally — no data is sent anywhere.

## Install & use

1. Download **DiscordStereo.exe** from the [latest release](../../releases/latest).
2. Double-click it. A simple menu appears:
   - **1) Install and start Discord** — applies the patch and launches Discord.
   - **2) Uninstall** — restores everything to original.
   - **3) Auto-start** — apply automatically at every Windows login.
   - **4) Language** — English / Türkçe.
3. When Discord opens, join a voice channel and play your stereo source. The other person should listen **with headphones** to hear the left/right separation.

> If Windows SmartScreen warns you (unsigned app): **More info → Run anyway**. See [verifying the download](#verifying-the-download) to check the file hash.
>
> If Discord is running **as administrator**, the tool cannot close it — right-click the tray icon → *Quit Discord*, then try again.

### Command line (optional)

```
DiscordStereo.exe --apply         # patch and launch
DiscordStereo.exe --restore       # restore original
DiscordStereo.exe --status        # show status
DiscordStereo.exe --install       # run automatically at every login
DiscordStereo.exe --uninstall     # remove auto-start
DiscordStereo.exe --bitrate 384000    # set bitrate (8000–510000, default 510000)
DiscordStereo.exe --lang en|tr        # force language
```

Your language and bitrate choices are remembered in `%LOCALAPPDATA%\DiscordStereo\settings.txt`.

## Best-quality Discord settings

The patch already disables in-app processing, but for good measure, in Discord → **Voice & Video**:

- **Noise Suppression (Krisp): Off**
- **Echo Cancellation: Off**
- **Automatic Gain Control: Off**
- **Advanced voice activity / audio signal processing: Off**

Use headphones (with echo cancellation off, speakers may cause echo for others).

## How it works

Two layers work together:

1. **JS hook** (`index.js`): sets the voice encoder to 2 channels and the chosen bitrate, and turns off the processing (noise/echo/AGC) that collapses stereo — so the mic is **captured** as 2 channels.
2. **Native patches** (`discord_voice.node`, signature-based, 3 sites):
   - *Stereo*: channel count 1 → 2 in the send stream.
   - *Downmix bypass*: skips the branch that downmixes the captured 2 channels to mono (the actual fix).
   - *High-pass off*: disables the low-cut filter that hurts music.

Every change is **backed up** to `%LOCALAPPDATA%\DiscordStereo\backup\` with the original SHA-256 in the filename; *Uninstall* restores byte-for-byte. If a signature does not match exactly once, nothing is written (it never touches the wrong version).

## Troubleshooting / FAQ

**The other person still hears mono.**
- Make sure they listen with **headphones** — speakers blur left/right.
- Your source must actually be stereo with distinct L/R (test with a track that has clear panning).
- Re-check `--status` shows **STEREO ACTIVE**. If Discord updated, re-run the tool.

**It worked, then stopped after I restarted Discord.**
- Launch Discord **through this tool** (menu → *Install and start*) or enable **auto-start**. Discord can revert the on-disk JS hook on some manual relaunches; the tool re-applies it.

**Bitrate seems capped / quality lower than 510 kbps.**
- Discord enforces a **server-side maximum bitrate per voice channel**. Non-boosted servers cap lower (often 64–96 kbps); boosted servers allow more. The tool requests high bitrate client-side, but the server may still cap it. This is a Discord limit, not a bug.

**"Could not close Discord."**
- Discord is running as administrator. Quit it from the tray (*Quit Discord*) and retry, or run this tool as administrator too.

**Antivirus / SmartScreen flags the exe.**
- The binary is unsigned and patches another app's files, which commonly triggers heuristics. Build it yourself from source (below), or verify the published hash.

**Does it work on PTB / Canary?**
- The tool detects Stable, PTB, Canary and Development, newest first. It is primarily tested on Stable.

## Verifying the download

Each release lists the SHA-256 of `DiscordStereo.exe` (see `SHA256SUMS.txt` on the release). To check:

```powershell
Get-FileHash .\DiscordStereo.exe -Algorithm SHA256
```

Compare it with the value on the release page.

## Build from source

Uses the C# compiler that ships with .NET Framework 4.x — no external dependencies:

```
build.cmd
```

Or with the .NET SDK:

```
dotnet build -c Release
```

This produces `DiscordStereo.exe`.

## Persistence & Discord updates

- When Discord updates itself, the module files are replaced and the patch is gone. With **auto-start** enabled, the tool detects the new version and re-applies on the next login.
- For the most reliable result, open Discord **through this tool** (or via auto-start) rather than the normal shortcut.

## Credits

Inspired by the community's stereo work (edoStereo; DiscordVoicePatcher / Vencord voicePatcher). This is an independent, single-file, version-independent implementation.

## License

MIT — see [LICENSE](LICENSE).
