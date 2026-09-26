# Roadmap

Goal: the most reliable, maintainable, cross-platform way to send true stereo through Discord's mic channel — and stay working as Discord updates.

## Done — v1.x (Windows)
- Signature-based native patches (stereo / downmix bypass / high-pass off) → **version-independent**.
- JS hook for `channels = 2` + high bitrate + low latency.
- Single self-contained exe: menu, `--apply/--restore/--status/--verify/--selftest`, auto-start.
- Backups + byte-for-byte restore, EN/TR UI, CI build + published SHA-256, self-test.

## v1.2 — Fully native durability (Windows)
- **Force `channels = 2` natively** so the JS hook is no longer required. The on-disk JS hook can be reverted by Discord on some relaunches; doing it in `discord_voice.node` (which survives) makes stereo durable across any launch. Tracked in [#1](../../issues/1). Must be crash-verified on a live session before shipping.
- Multiple fallback signatures per patch site, so a single Discord codegen change doesn't break a patch.

## v2.0 — Linux
- Detect Discord on Linux (`~/.config/discord*`, Flatpak, `/opt/...`).
- Parse the ELF `discord_voice.node` and resolve the same patch sites by signature (System V ABI / different registers → new signatures).
- Portable runtime (retarget to cross-platform .NET or a small companion).
- **Verification required on a real Linux install before it is called supported.**

## v2.1 — macOS
- Detect Discord under `~/Library/Application Support/discord*`.
- Parse the Mach-O `discord_voice.node`; note code-signing/SIP implications of modifying a signed bundle.
- Verify on a real macOS install.

## Quality / trust
- A short demo (GIF) in the README.
- A test matrix across Discord Stable/PTB/Canary and a few versions.
- Signature-health command that reports exactly which sites still match on the current build.

## Non-goals
- Bypassing Discord's server-side per-channel bitrate cap (a server limit, not a client one).
- Shipping large prebuilt/patched binaries. We patch the user's own original file, with backups.
