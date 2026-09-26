# Contributing

Thanks for helping keep Discord Stereo working across Discord versions and platforms.

## Build & test

```
build.cmd            # or: dotnet build -c Release
DiscordStereo.exe --selftest    # validate patch defs + JS template + in-memory apply
DiscordStereo.exe --status      # detected install + patch state
DiscordStereo.exe --verify      # confirm real stereo from Discord's own logs (after joining voice)
```

CI (`.github/workflows/build.yml`) compiles the exe from source, runs a sanity check (`node --check` on the embedded JS hook), and publishes a SHA-256 on tagged releases.

## When a Discord update breaks a patch

The tool is signature-based, so most updates keep working. If `--status` shows a site as *NoSig* or *unexpected*:

1. Grab the new `discord_voice.node` from
   `%LOCALAPPDATA%\Discord\app-<version>\modules\discord_voice-*\discord_voice\`.
2. Find the code around the broken site (the three patches are documented in `src/Program.cs` and issue [#1](../../issues/1)):
   - **Stereo**: where the send-stream channel count is set to 1 (make it 2).
   - **Downmix bypass**: the branch that downmixes captured audio to mono (skip it).
   - **High-pass off**: the high-pass filter function (neutralise it).
3. Build a **wildcard signature** that matches the surrounding bytes exactly once, plus the `SigOffset`, `Verify`, and `Write` bytes. Keep wildcards on relative offsets/registers that shift between builds.
4. Add or adjust the entry in `NativePatches` in `src/Program.cs`, run `--selftest`, then verify on a live voice session with `--verify` (Opus `stereo=1`, captured `channels=2`) and confirm no crash.

Prefer signatures that are robust across builds over hardcoded offsets — hardcoded offsets are exactly what makes older tools break on every update.

## Porting to Linux / macOS

Same patch sites, different binary format and ABI:

- **Linux**: ELF `discord_voice.node`; System V AMD64 ABI (first args in `rdi, rsi, rdx, rcx, r8, r9`).
- **macOS**: Mach-O `discord_voice.node`; note the client is code-signed — modifying it has SIP/signing implications.

Derive the platform's signatures from a real install, gate them per-OS, and **only mark a platform supported after verifying stereo on that platform**. Do not ship unverified patches as working.

## Ground rules

- Never commit patched or prebuilt Discord binaries. Patch the user's own file at runtime, with a backup.
- Keep everything local — no network calls, no telemetry.
- Any patch must be reversible (backup + byte-for-byte restore) and must refuse to write when its signature is not an exact single match.
