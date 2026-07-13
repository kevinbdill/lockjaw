# Changelog

All notable changes to Lockjaw. Format follows [Keep a Changelog](https://keepachangelog.com/);
versions follow [SemVer](https://semver.org/) (0.x = pre-audit development).

## [0.2.0] — 2026-07-13

### Added
- **Graphical interface** (Avalonia): drag-and-drop window for encrypting and
  decrypting — drop files or folders, enter passphrase, done.
- Automatic mode detection: dropping a `.lockjaw` file switches the window to
  decrypt mode.
- "Open with Lockjaw" support for `.lockjaw` files from Explorer.
- Application icon.

### Notes
- Single self-contained `Lockjaw.exe` (~44 MB), no .NET install required.
- Known limitation: the click-to-browse dialog selects files only; use
  drag-and-drop for folders.
- Still unsigned — SmartScreen "unknown publisher" warning is expected.

## [0.1.0] — 2026-07-13

Initial public release (M1: core engine + CLI).

### Added
- `LockjawCore` engine: XChaCha20-Poly1305 secretstream (1 MiB authenticated
  chunks, mandatory final tag), Argon2id13 passphrase derivation
  (256 MiB / 3 ops, parameters stored in header), complete public-header
  authentication as associated data.
- Streaming PAX tar archiving with safe extraction (rejects path traversal,
  absolute paths, symlinks, reparse points).
- Optional DEFLATE compression before encryption.
- Binary `.lockjaw` and Base64 armored `.lockjaw.txt` output.
- Atomic output staging: no partial plaintext ever published; existing
  outputs never overwritten.
- CLI: `encrypt`, `decrypt`, `inspect`; interactive no-echo passphrase
  prompt; `LOCKJAW_PASSPHRASE` for automation; deliberate single error
  message/exit code for wrong-passphrase and tamper (no oracle).
- xUnit test suite; container format documentation; security policy with
  v1.0 release gates; BIS export-notification template.
