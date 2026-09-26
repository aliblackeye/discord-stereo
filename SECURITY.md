# Security & privacy

## What this tool does

Discord Stereo modifies two files inside your local Discord installation's voice module:

- `discord_voice.node` — three signature-based byte patches.
- `index.js` — a small JavaScript hook.

Both are inside `%LOCALAPPDATA%\Discord*\app-*\modules\discord_voice-*\discord_voice\`.

## Privacy

- **Everything runs locally.** The tool makes no network requests. It contains no telemetry, analytics, or auto-update.
- It reads only the Discord install files it patches and writes only its own backups/settings under `%LOCALAPPDATA%\DiscordStereo\`.

## Safety

- Before any change, the original file is copied to `%LOCALAPPDATA%\DiscordStereo\backup\<channel>-<version>\` with the original SHA-256 in its name.
- A patch is written only if its signature matches **exactly once** and the target bytes are exactly what is expected. Otherwise nothing is written.
- *Uninstall* / `--restore` copies the verified backup back byte-for-byte.
- The tool needs no administrator rights for a normally-installed Discord.

## Verifying the binary

Prefer building from source (`build.cmd` or `dotnet build`). If you use a released `DiscordStereo.exe`, verify its hash against `SHA256SUMS.txt` on the release:

```powershell
Get-FileHash .\DiscordStereo.exe -Algorithm SHA256
```

Released binaries are built by GitHub Actions from the tagged source.

## Disclaimer

This tool modifies your own Discord client. Client modifications may violate Discord's Terms of Service. Use at your own risk; the authors accept no liability.

## Reporting an issue

Open a GitHub issue. Please do not include credentials or personal data.
