# Security policy

## Current status

Lockjaw M1 is a development build. It has not been independently audited. The
cryptographic primitives come from libsodium, but protocol composition,
container parsing, archive extraction, native packaging, and user experience
still require review.

Do not publish a v1.0 release until all of these gates are complete:

1. Build and run the test suite on clean Windows 10 and Windows 11 machines.
2. Freeze the v1 byte format and add a committed decryption compatibility
   vector made by the release candidate.
3. Run multi-gigabyte, low-disk-space, cancellation, and power-loss tests.
4. Add malformed-container fuzzing and run it for at least 24 hours.
5. Obtain independent review of `LockjawCore`, especially framing and staging.
6. Review and freeze the exact .NET libsodium binding and native library build.
7. Verify that the published executable makes no outbound connections.
8. Complete a current export-control review for the real publication channel.

## Intended guarantees

- Confidentiality and integrity through XChaCha20-Poly1305 secretstream
- Header authentication using the complete public prefix as associated data
- Truncation detection through a required final secretstream tag
- No published partial plaintext after authentication or extraction failure
- No silent overwrite of existing output
- No archive path traversal or link extraction

## Important limits

- A weak passphrase remains vulnerable to guessing; Argon2id raises the cost but
  cannot make a weak secret strong.
- .NET strings are immutable. The interactive CLI collects passphrases in
  erasable character arrays, but `LOCKJAW_PASSPHRASE` first exists as a managed
  environment string and cannot be reliably wiped.
- File deletion is not guaranteed forensic erasure on journaled filesystems,
  SSDs, snapshots, backups, or virtual disks.
- Malware, a compromised OS, keyloggers, process dumps, and screen capture are
  outside the protection offered by a local file-encryption program.
- Metadata outside the payload still reveals the Lockjaw signature, format version,
  mode, KDF cost, chunk size, and approximate encrypted size.

## Reporting a vulnerability

Before a public repository exists, report suspected vulnerabilities privately
to the project owner. Do not include real secrets or sensitive plaintext in a
report. Once hosting is selected, replace this paragraph with a dedicated
security contact and coordinated-disclosure window.

