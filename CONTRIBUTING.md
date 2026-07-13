# Contributing to Lockjaw

Thanks for your interest! Lockjaw is a small, security-focused project, so
contributions are welcome but held to a deliberate bar.

## Ground rules

1. **No new crypto.** All cryptography comes from libsodium via the pinned
   binding. PRs adding cipher options, alternative KDFs, or hand-rolled
   primitives will be declined — one good default beats a menu of footguns.
2. **Core stays clean.** `src/LockjawCore` has zero UI and zero networking
   dependencies. Ever.
3. **Tests first.** Changes to `LockjawCore` need test coverage. All tests
   must pass (`dotnet test -c Release`).
4. **Don't weaken deliberate behaviors.** The following are features, not
   bugs — PRs "fixing" them will be declined:
   - Wrong passphrase and tampered file produce the *same* error (no oracle)
   - Existing outputs are never overwritten
   - No partial plaintext is ever published on failure
   - No telemetry, update checks, or network code of any kind
5. **Honest claims only.** Documentation must not describe Lockjaw as
   audited, production-ready, or "military-grade." It is a well-designed
   prototype until independent review says otherwise.

## Getting started

See [docs/BUILDING.md](docs/BUILDING.md) for setup, project layout, and the
test/release process. The short version:

```bash
git clone https://github.com/kevinbdill/lockjaw
cd lockjaw
dotnet restore && dotnet test -c Release
```

## Pull requests

- Keep PRs focused — one change per PR.
- Describe *why*, not just what.
- For anything touching `LockjawHeader.cs`, `ArchiveCodec.cs` extraction,
  or the staging/rename logic in `StreamHelpers.cs`, expect careful review;
  these parse attacker-controlled input or enforce the safety contract.
- Format changes require a header version bump and backward-compatible
  reading — see "Format stability" in the building guide.

## Reporting security issues

Please **do not** open a public issue for vulnerabilities. See
[SECURITY.md](SECURITY.md) for reporting guidance. Good-faith research on
your own files/machines is welcome.

## Reporting bugs

Open a GitHub issue with:
- Lockjaw version (release tag or commit)
- Windows version
- Exact steps, expected vs. actual behavior
- The exact error text if any (never include your passphrase or sensitive
  file contents)

## License

By contributing you agree your contributions are licensed under the MIT
license that covers the project.
