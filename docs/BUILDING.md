# Building & Developing Lockjaw

Everything needed to build, test, and release Lockjaw from source.

## Prerequisites

- **.NET 8 SDK** (any 8.x — `global.json` uses `rollForward: latestFeature`)
  - Windows: https://dotnet.microsoft.com/download/dotnet/8.0
  - Ubuntu/Debian: `sudo apt-get install dotnet-sdk-8.0`
- Git

No other dependencies. The libsodium native library ships via the
`LibSodium.Net` NuGet package.

## Project layout

```
Lockjaw.sln
Directory.Build.props        shared build settings (nullable, warnings-as-errors)
global.json                  SDK pin
src/
  LockjawCore/               the engine — everything security-critical lives here
    LockjawConstants.cs        magic bytes, format version, extensions, defaults
    LockjawHeader.cs           container header parse/serialize
    LockjawService.cs          top-level Encrypt/Decrypt/Inspect API
    PassphraseKey.cs           Argon2id derivation, sodium secure memory
    SecretStreamEncryptingWriteStream.cs   chunked XChaCha20-Poly1305 streaming
    ArchiveCodec.cs            PAX tar creation + safe extraction
    ArmorCodec.cs              Base64 armor encode/decode
    StreamHelpers.cs           atomic staging/rename helpers
  LockjawCli/                console front-end (thin wrapper over LockjawService)
  LockjawApp/                Avalonia GUI (drag-drop window)
tests/
  LockjawCore.Tests/         xUnit suite (round-trip, tamper, wrong-pass, armor…)
docs/                        specs and guides
compliance/                  BIS export notification template
test-vectors/                compatibility vectors (policy in its README)
```

Design rule: **`LockjawCore` has no UI dependencies and no networking.** The
CLI and GUI are thin shells; all cryptographic and file-safety logic is in the
core so it is tested once and shared.

## Build and test

```bash
dotnet restore
dotnet build
dotnet test -c Release        # all tests must pass before any commit to main
```

Note: `LockjawCli.csproj` defaults its `RuntimeIdentifier` to `win-x64`, so a
plain `dotnet build` on Linux puts CLI output under `bin/.../win-x64/`. For
local Linux testing, publish explicitly:

```bash
dotnet publish src/LockjawCli -c Release -r linux-x64 --self-contained -o /tmp/lj-linux
/tmp/lj-linux/Lockjaw encrypt ./somefolder -o /tmp/test.lockjaw   # LOCKJAW_PASSPHRASE env var for scripting
```

## Publishing Windows binaries

Both front-ends publish self-contained — the user needs no .NET runtime:

```bash
# GUI — folder publish. WARNING: do NOT add -p:PublishSingleFile=true here.
# Single-file publish crashes the Avalonia GUI at startup on Windows
# (v0.2.0 shipped that way and failed to open; fixed in v0.2.1 by shipping
# the publish folder). Ship the whole folder as the release zip.
dotnet publish src/LockjawApp -c Release -r win-x64 --self-contained -o out/win-gui

# CLI
dotnet publish src/LockjawCli -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o out/win-cli
```

The Windows exe **cross-compiles from Linux** without issue (the GUI is
Avalonia, not WPF, precisely so the whole project builds anywhere). The CLI
publishes as a single ~35 MB `Lockjaw.exe`; the GUI publishes as a folder
(~97 MB, zips to ~44 MB) whose `Lockjaw.exe` must stay with its neighbors.
Delete the `*.pdb` files from the GUI folder before zipping a release.

## Testing philosophy

The xUnit suite in `tests/LockjawCore.Tests` covers:

- **Round-trip:** encrypt → decrypt → byte-identical output (files & folders)
- **Tamper detection:** a single flipped ciphertext byte must fail with
  `Incorrect passphrase or damaged file.` and produce **no partial output**
- **Wrong passphrase:** must be indistinguishable from tampering (same
  message, same exit path) — do not "improve" this
- **Armor:** Base64 round-trip
- **Header inspection** without passphrase

When adding features, extend the suite first. Any change to
`LockjawCore` requires the full suite green plus a manual round-trip on a
real multi-megabyte folder.

### Manual pre-release verification gate

Run all four on every release candidate:

1. Round-trip a real folder; compare hashes (`md5sum`/`Get-FileHash`)
2. Flip one byte mid-file → exit 1, standard message, no partial output dir
3. Wrong passphrase → identical behavior to (2)
4. `Lockjaw inspect` prints header without asking for a passphrase

## Release process

1. Ensure tests green and the manual gate above passes.
2. Update `CHANGELOG.md`.
3. Publish the Windows exe(s), zip as `Lockjaw-<version>-win-x64.zip`.
4. Compute and record the SHA-256 of `Lockjaw.exe`.
5. Tag `v<version>`, create the GitHub release, attach the zip, and put the
   SHA-256 + SmartScreen note in the release body.
6. lockjaw.best's Download button points at `releases/latest` — no site
   change needed for a normal release.

## Format stability

The container format (`docs/FORMAT.md`) is versioned. Format v1 is not yet
frozen — freezing plus a committed compatibility vector is a v1.0 release
gate (see `SECURITY.md` and `test-vectors/README.md`). Until then, any format
change must bump the header version and keep the decryptor able to read all
prior versions ever shipped in a release.

## Dependency policy

- **LibSodium.Net** is pinned (currently `0.16.0-alpha`) because it exposes
  secretstream, Argon2id, and secure-memory APIs on .NET 8. Do not upgrade it
  casually — moving to a stable binding (or freezing and reviewing the exact
  binding source) is a release gate, not a routine package bump.
- No other runtime dependencies in `LockjawCore`. Keep it that way: every new
  dependency in the core is attack surface.
- The GUI uses Avalonia; UI-only dependencies are acceptable in
  `LockjawApp` but must never leak into the core.

## Security-sensitive areas

Take extra care (and expect extra review) when touching:

- `LockjawHeader.cs` — parsing attacker-controlled bytes
- `ArchiveCodec.cs` extraction — path traversal / link defenses
- `StreamHelpers.cs` — the atomic staging/rename contract ("no partial
  plaintext ever published")
- Anything that changes an error message on the authentication-failure path
  (the wrong-passphrase / tamper indistinguishability is deliberate)

See [SECURITY.md](../SECURITY.md) for the full policy and v1.0 release gates.
