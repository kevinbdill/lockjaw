# Lockjaw

**Website:** [lockjaw.best](https://lockjaw.best) · **License:** MIT · **Crypto:** libsodium (XChaCha20-Poly1305 + Argon2id)

Lockjaw is an offline file-encryption application for Windows. This repository is
the M1 implementation: a shared .NET 8 core plus a scriptable CLI for
passphrase encryption, decryption, inspection, compression, and armored text.

> **Security status:** development prototype. The design uses libsodium rather
> than custom cryptography, but this application and its container format have
> not received an independent security audit. Do not call it production-ready
> or use it as the only protection for irreplaceable data yet.

## What M1 implements

- XChaCha20-Poly1305 authenticated streaming through libsodium secretstream
- Argon2id13 passphrase derivation (256 MiB / 3 operations by default)
- 1 MiB authenticated chunks with a mandatory final tag
- streaming PAX tar archive creation and safe extraction
- optional DEFLATE compression before encryption
- binary `.lockjaw` and Base64 armored `.lockjaw.txt` output
- atomic final output: failed operations do not publish a destination
- complete public-header authentication on every encrypted chunk
- keys and secretstream state held in sodium-backed secure memory
- no networking code, telemetry, update checks, or cloud dependency

Recipient public keys, key management, and the WPF application are later
milestones and are not stubbed with weaker substitutes in M1.

## Build and test

Install the .NET 8 SDK, then run:

```powershell
dotnet restore
dotnet test -c Release
dotnet publish src/LockjawCli/LockjawCli.csproj -c Release -r win-x64 --self-contained true
```

The publish result is under
`src/LockjawCli/bin/Release/net8.0/win-x64/publish/Lockjaw.exe`. The project is
configured as a self-contained single-file publish. Its native libsodium
component may be extracted by the .NET host at runtime; only `Lockjaw.exe` needs
to be distributed.

The project pins `LibSodium.Net` `0.16.0-alpha` because it exposes the required
secretstream, Argon2id, and secure-memory APIs on .NET 8. Moving to a stable,
maintained binding—or freezing and reviewing the exact binding source—is a
release gate, not an automatic package upgrade.

## CLI

```text
Lockjaw encrypt <paths...> [-o out.lockjaw] [-p] [--armor] [--no-compress]
Lockjaw decrypt <file.lockjaw> [-o outdir] [-p]
Lockjaw inspect <file.lockjaw>
```

Examples:

```powershell
Lockjaw encrypt .\QuarterlyReports -p
Lockjaw encrypt .\report.pdf -o report-email.lockjaw.txt -p --armor
Lockjaw decrypt .\QuarterlyReports.lockjaw -o .\Recovered -p
Lockjaw inspect .\QuarterlyReports.lockjaw
```

The interactive prompt does not echo the passphrase. Automation can set
`LOCKJAW_PASSPHRASE`, but environment variables are less safe because other
process-management and logging tools may expose them.

Authentication failures deliberately return one message—`Incorrect passphrase
or damaged file.`—and one exit code (`1`). Lockjaw must not tell an attacker whether
a candidate passphrase was correct. Malformed or unsupported public containers
return `2`; I/O errors return `3`.

## Safety behavior

- Existing output files and directories are refused.
- Encryption writes a sibling temporary file and renames it only after success.
- Decryption authenticates the complete encrypted stream into a delete-on-close
  staging file before creating any extracted output.
- Archive extraction occurs in a new sibling staging directory. Lockjaw rejects
  absolute paths, `..`, duplicate paths, symbolic links, and reparse points.
- The final directory rename is same-volume and atomic.
- Cancellation and authentication failure trigger staging cleanup.

Operating systems and SSDs cannot promise physical secure erasure after a file
is deleted. Lockjaw promises no published partial plaintext and performs cleanup;
it does not claim forensic erasure of storage media.

## Documents

- [Container format](docs/FORMAT.md)
- [M1 design review and spec corrections](docs/SPEC_REVIEW.md)
- [Security policy and release gates](SECURITY.md)
- [Compatibility-vector policy](test-vectors/README.md)

## License

MIT. Export-control and jurisdictional obligations must be reviewed against the
actual distribution method and public repository before release; the source
code does not present the development-spec legal summary as legal advice.

