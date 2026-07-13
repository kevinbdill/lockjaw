# Lockjaw — File Encryption for Windows
## Development Specification v1.0
*(Working name "Lockjaw" — swap globally if another name is chosen. File extension `.lockjaw`; all references parameterized in one constants module.)*

---

## 1. Product Summary

A Windows desktop application (GUI + companion CLI) that encrypts files and folders with modern authenticated encryption so they can be safely emailed or shared. Spiritual successor to Kent Briggs' "Puffer," rebuilt on current cryptography.

**Core promise:** Drag files on → enter passphrase (or pick recipient) → get one `.lockjaw` file. Recipient opens it with the free app → original files come back, byte-identical, with proof nothing was tampered with.

**Non-goals (v1):** self-decrypting EXEs, cloud storage, key servers, mobile apps, macOS/Linux GUI (CLI should compile cross-platform but only Windows is a v1 deliverable).

---

## 2. Cryptographic Design

All cryptography via **libsodium** (or its .NET binding). No hand-rolled primitives. No configurable cipher menus — one good default, versioned for future migration.

### 2.1 Primitives
| Purpose | Algorithm | libsodium API |
|---|---|---|
| Symmetric encryption | XChaCha20-Poly1305 (AEAD) | `crypto_secretstream_xchacha20poly1305_*` |
| Passphrase → key | Argon2id | `crypto_pwhash` (alg `ARGON2ID13`) |
| Public-key mode | X25519 + XChaCha20-Poly1305 | `crypto_box_seal` / `crypto_kx` |
| Hashing/fingerprints | BLAKE2b | `crypto_generichash` |
| Random | `randombytes_buf` | — |

### 2.2 Argon2id parameters (v1 defaults)
- Memory: 256 MiB, Iterations: 3, Parallelism: 4
- Parameters are **stored in the file header** so future versions can raise defaults without breaking old files.
- If the machine has < 1 GB free RAM, fall back to 64 MiB / 4 iterations (still stored in header).

### 2.3 Encryption pipeline
```
input files/folders
  → tar-style archive (single stream, preserves paths + timestamps)
  → DEFLATE compression (zlib level 6; skip if entropy test says incompressible)
  → chunked AEAD encryption via secretstream (1 MiB chunks)
  → .lockjaw container
```
Compression happens **before** encryption (encrypted data is incompressible). Chunked secretstream means: constant memory for multi-GB files, per-chunk authentication, truncation detection via final tag.

### 2.4 Two encryption modes
1. **Passphrase mode** — sender and recipient share a passphrase out-of-band. Key = Argon2id(passphrase, random 16-byte salt).
2. **Keypair mode** — recipient generates an X25519 keypair once; shares the public key (short Base58 string, ~50 chars, QR-exportable). Sender encrypts to that public key; no shared secret ever transmitted. A file may target **multiple recipients** (per-recipient sealed copies of the file key, like age/PGP).

### 2.5 File format (`.lockjaw` container)
```
[ magic: "LOCKJAW" (4 bytes) ]
[ format version: u16 ]
[ mode: u8 (1=passphrase, 2=keypair) ]
[ header length: u32 ]
[ header (mode-dependent):
    passphrase: argon2 params + salt
    keypair:    recipient stanzas (ephemeral pk + sealed file key) × N ]
[ secretstream header (24 bytes) ]
[ encrypted chunks... each = 1 MiB plaintext + 17-byte AEAD tag ]
[ final chunk carries TAG_FINAL — absence = truncated file ]
```
- Header is included as **associated data** in chunk 0 → header tampering breaks decryption.
- No filenames, sizes, or metadata visible outside the encrypted payload. Only thing an observer learns: it's a Lockjaw file, its version, mode, and approximate size.

### 2.6 Explicit security properties
- Confidentiality + integrity + authenticity (AEAD; any modified bit = hard failure, never partial output)
- Wrong passphrase = clean error, cryptographically indistinguishable from corruption
- Memory hygiene: keys in locked memory (`sodium_mlock`), zeroed after use (`sodium_memzero`)
- No telemetry, no network calls, period. The app works fully offline.

---

## 3. Applications

### 3.1 GUI (primary deliverable)
**Stack:** C# / .NET 8, WPF (or WinUI 3), `libsodium-core` NuGet binding. Single-file self-contained publish → one `Lockjaw.exe`, no installer required (optional MSIX/Inno installer as stretch).

**Main window:**
- Large drop zone: "Drop files or folders here" (+ Browse button)
- Mode toggle: Passphrase | Recipients
- Passphrase entry: two masked fields (confirm), show/hide eye, **zxcvbn strength meter** with honest crack-time estimate; warn (not block) below score 3
- Recipients mode: pick from saved contacts (imported public keys) and/or paste a key
- Output: same folder as input by default, `name.lockjaw`; Save-As override
- Options (collapsed by default): compression on/off, armor (text) output, delete originals after encrypt (uses Recycle Bin, with confirmation)

**Decrypt flow:**
- Dropping a `.lockjaw` file (or double-clicking one, via file association) switches to decrypt mode
- Prompts passphrase or auto-selects matching private key from local keyring
- Extracts to chosen folder; refuses to overwrite silently (rename `(1)` or ask)
- Progress bar with cancel for large files; cancel leaves no partial plaintext on disk (write to temp, atomic rename on success)

**Key manager (Recipients mode support):**
- Generate "My Identity" (X25519 keypair). Private key stored encrypted-at-rest under a local passphrase + DPAPI, in `%APPDATA%\Lockjaw\keys\`
- Export my public key (copy string / save `.lockjawkey` / QR code)
- Import contacts' public keys with a friendly name; show BLAKE2b fingerprint for out-of-band verification
- Backup/restore identity (encrypted export file); large red warnings that a lost private key = lost files

**Shell integration:**
- Right-click context menu: "Encrypt with Lockjaw" / "Decrypt with Lockjaw" (registry entries; toggle in settings)
- `.lockjaw` file association with app icon (a pufferfish, naturally)

### 3.2 CLI (`lockjaw.exe`, same core library)
```
lockjaw encrypt  <paths...> [-o out.lockjaw] [-p | -r <pubkey|contact>...] [--armor] [--no-compress]
lockjaw decrypt  <file.lockjaw> [-o outdir] [-p | -i identityfile]
lockjaw keygen   [-o identity.lockjawkey]
lockjaw pubkey   [-i identityfile]       # print public key
lockjaw inspect  <file.lockjaw>             # header info only, no secrets
```
- Passphrase via interactive prompt or `LOCKJAW_PASSPHRASE` env var (documented as less safe)
- Exit codes: 0 ok, 1 bad passphrase/key, 2 corrupt/tampered, 3 I/O error
- Deterministic, scriptable, no prompts when fully specified (automation-friendly)

### 3.3 Armor mode
`--armor` / GUI checkbox emits Base64 text:
```
-----BEGIN LOCKJAW ENCRYPTED FILE-----
<base64, 64-char lines>
-----END LOCKJAW ENCRYPTED FILE-----
```
Pasteable into an email body; decryptor accepts armored input transparently (auto-detect). ~33% size overhead; recommend only for < 5 MB payloads (soft warning).

---

## 4. Architecture

```
LockjawCore   (class library — all crypto, container format, archive/compress; ZERO UI deps)
   ├─ LockjawCli   (thin console wrapper)
   └─ LockjawApp   (WPF GUI)
```
- LockjawCore is the only code that touches libsodium. 100% of the file-format logic lives here.
- Golden-file test vectors committed to repo: known key + known input → exact expected `.lockjaw` bytes (and reverse). Any format drift fails CI.
- Fuzz target: decryptor must never crash or emit plaintext on malformed input (property: reject ≠ crash).

---

## 5. Error Handling & UX Rules

- Wrong passphrase → "Incorrect passphrase or damaged file." (never distinguish — that's an oracle)
- Truncated/tampered → "This file is incomplete or has been modified. Nothing was extracted."
- Never write partial plaintext: decrypt to `%TEMP%` staging, atomic move on full success, secure-delete staging on failure
- Large-file support: tested to 10 GB; memory ceiling ~300 MB regardless of input size
- All user-facing text in a resource file (future localization)

---

## 6. Distribution & Legal

- **License:** open source (MIT or BSD-2) — enables the BIS route below and builds trust
- **Export compliance:** one-time email notification to crypto@bis.doc.gov + ERN per EAR §742.15(b) / §734.3(b)(3) with the public repo URL. That is the entire requirement for publicly available open-source encryption. Template letter included in repo (`/compliance/BIS_NOTIFICATION.md`).
- **Code signing:** Authenticode-sign `Lockjaw.exe` (OV cert ~$100/yr, or start unsigned + SmartScreen reputation warning documented in README)
- **Website/README must state:** software provided as-is, users responsible for local law in their jurisdiction (some countries restrict crypto import/use)
- No trademark filing needed for v1; do a knockout search on the final name before publishing

---

## 7. Milestones

| # | Deliverable | Est. effort |
|---|---|---|
| M1 | LockjawCore: container format, passphrase mode, encrypt/decrypt round-trip + golden tests | 2–3 days |
| M2 | CLI complete (all verbs), armor mode, 10 GB stress test | 1–2 days |
| M3 | GUI: drop zone, passphrase flows, progress, file association | 3–4 days |
| M4 | Keypair mode: keygen, keyring, multi-recipient, contact manager | 2–3 days |
| M5 | Shell context menu, polish, icon, error-message pass, README + BIS letter | 1–2 days |
| M6 | Fuzzing pass, third-party eyes on LockjawCore, tag v1.0, sign, release | 1–2 days |

**Total: ~2 weeks of focused effort** for a signed, releasable v1. M1+M2 alone yield a fully usable CLI in under a week.

---

## 8. Acceptance Criteria (v1 ships when…)

1. Round-trip: any file/folder set up to 10 GB encrypts and decrypts byte-identical (hash-verified) on Windows 10 & 11
2. Flipping any single bit anywhere in a `.lockjaw` file causes decryption to fail cleanly with zero plaintext output
3. Wrong passphrase and corrupted file produce indistinguishable errors
4. A non-technical user can encrypt a folder and email it in under 60 seconds with no documentation
5. Recipient with only `Lockjaw.exe` and the passphrase recovers the files, no install, no admin rights
6. Golden test vectors pass; fuzz run (24 h min) with zero crashes
7. GUI never freezes: all crypto on background threads, cancel works, no partial output ever left behind
8. Works fully offline; zero outbound network connections (verified with firewall log during test pass)

---

## 9. Future (post-v1 parking lot)
- macOS/Linux CLI builds (LockjawCore is portable by design)
- Armor QR codes for tiny secrets
- Steganographic filename mode / decoy volumes — *only if demand*
- Localizations
- MSIX Store distribution
