# Lockjaw User Guide

This guide covers everything a user needs: the GUI, the CLI, what Lockjaw
protects against, and what it does not.

## Contents

1. [Installing](#installing)
2. [Using the GUI](#using-the-gui)
3. [Using the CLI](#using-the-cli)
4. [Sharing encrypted files](#sharing-encrypted-files)
5. [Choosing a passphrase](#choosing-a-passphrase)
6. [What Lockjaw protects — and what it doesn't](#what-lockjaw-protects--and-what-it-doesnt)
7. [Troubleshooting](#troubleshooting)

---

## Installing

1. Download `Lockjaw-<version>-win-x64.zip` from the
   [latest release](https://github.com/kevinbdill/lockjaw/releases/latest).
2. (Recommended) Verify the SHA-256 of `Lockjaw.exe` against the value in the
   release notes:
   ```powershell
   Get-FileHash .\Lockjaw.exe -Algorithm SHA256
   ```
3. Unzip anywhere — `C:\Tools\Lockjaw\`, your Desktop, a USB stick. There is
   no installer, no registry changes, and no .NET runtime to install.

**First run:** Windows SmartScreen shows *"Windows protected your PC"* because
the executable is not yet code-signed. Click **More info → Run anyway**. This
is expected for unsigned open-source software; code signing is on the roadmap.

Lockjaw never connects to the internet. You can verify this with any firewall
or by running it on an air-gapped machine.

## Using the GUI

Double-click `Lockjaw.exe` to open the window.

### Encrypting

1. **Drag one or more files or folders** onto the drop zone. Folders are fully
   supported via drag-and-drop — the entire tree (subfolders, file names,
   timestamps) goes into the container. You can also click the drop zone to
   browse, though the browse dialog currently selects files only.
2. Enter a passphrase, and enter it again to confirm.
3. Click **Encrypt**.
4. A `.lockjaw` file appears next to your originals (for multiple inputs, one
   combined container).

Your original files are **not deleted** — Lockjaw creates an encrypted copy.
Delete the originals yourself if that is your goal, and remember that ordinary
deletion is not forensic erasure (see limits below).

### Decrypting

1. **Drop a `.lockjaw` file** onto the window — Lockjaw detects it and
   switches to decrypt mode automatically. Opening a `.lockjaw` file with
   *Open with → Lockjaw* in Explorer works too.
2. Enter the passphrase.
3. Click **Decrypt**. The original files and folder structure are restored
   next to the container.

### Errors

- **"Incorrect passphrase or damaged file."** — either the passphrase is wrong
  or the file has been modified/corrupted. Lockjaw deliberately cannot tell
  you which (revealing the difference would help attackers). Nothing partial
  is written.
- **Output already exists** — Lockjaw never overwrites existing files or
  folders. Move or rename the existing output and try again.

## Using the CLI

The CLI (`Lockjaw.exe` from the CLI release, or built from
`src/LockjawCli`) exposes the same engine for scripting:

```text
Lockjaw encrypt <paths...> [-o out.lockjaw] [-p] [--armor] [--no-compress]
Lockjaw decrypt <file.lockjaw> [-o outdir] [-p]
Lockjaw inspect <file.lockjaw>
```

| Option | Meaning |
|---|---|
| `-o <path>` | Output file (encrypt) or directory (decrypt) |
| `-p` | Prompt interactively for the passphrase (no echo) |
| `--armor` | Emit Base64 text (`.lockjaw.txt`) instead of binary |
| `--no-compress` | Skip DEFLATE compression (for already-compressed data) |

### Examples

```powershell
# Encrypt a folder, prompted for passphrase
Lockjaw encrypt .\QuarterlyReports -p

# Encrypt a PDF as armored text safe for any email system
Lockjaw encrypt .\report.pdf -o report-email.lockjaw.txt -p --armor

# Decrypt into a specific directory
Lockjaw decrypt .\QuarterlyReports.lockjaw -o .\Recovered -p

# Show container metadata (no passphrase needed)
Lockjaw inspect .\QuarterlyReports.lockjaw
```

### Scripting

For unattended use, set the passphrase in the `LOCKJAW_PASSPHRASE`
environment variable instead of `-p`. Be aware environment variables are
weaker than the interactive prompt: other tools on the machine (process
managers, loggers, crash dumps) may capture them.

Exit codes:

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Wrong passphrase **or** damaged/tampered file (indistinguishable by design) |
| 2 | Malformed or unsupported container |
| 3 | I/O error (including "output already exists") |

## Sharing encrypted files

- A `.lockjaw` file is safe to attach to email, upload to cloud storage, or
  put on a USB stick — it is opaque ciphertext. Observers cannot see file
  names, sizes, folder structure, or contents.
- If a mail system rejects binary attachments, use `--armor` to produce a
  plain-text container.
- Share the passphrase over a **different channel** than the file (e.g. file
  by email, passphrase by phone or in person). Never put both in one message.
- Most mail providers cap attachments around 25 MB; for larger containers use
  a file-sharing link.

## Choosing a passphrase

Argon2id makes each guess expensive (256 MiB of memory and real CPU time per
attempt), but it cannot rescue a weak secret. Use a long passphrase — four or
more random words, or 16+ mixed characters. Avoid anything guessable from
your life. **There is no recovery mechanism: a lost passphrase means the data
is gone.** That is a feature, not a bug — it means nobody else has a way in
either.

## What Lockjaw protects — and what it doesn't

**Protects against:**
- Anyone reading the encrypted file without the passphrase
- Undetected tampering — any modification (even one bit, including header
  fields) fails authentication
- Truncation — a cut-off file is detected via the mandatory final tag
- Leaking file names / folder structure — metadata is inside the ciphertext
- Malicious archives — extraction rejects path traversal, absolute paths,
  and symbolic links

**Does not protect against:**
- Weak passphrases (guessing remains possible, just expensive)
- Malware, keyloggers, or a compromised OS on the machine where you type the
  passphrase or view decrypted files
- Forensic recovery of *deleted originals* — SSDs, journaling filesystems,
  snapshots, and backups may retain data after deletion
- Someone who obtains both the file and the passphrase

## Troubleshooting

**"Windows protected your PC" on launch** — expected for an unsigned binary:
More info → Run anyway. If the button is missing, your organization's policy
(or Windows S mode) blocks unsigned apps.

**Antivirus quarantines the exe** — some AVs flag unsigned self-contained
.NET binaries. Check Windows Security → Protection history, restore, and add
an exclusion. Verify the SHA-256 first.

**A console window flashes and closes** — you have the CLI build (v0.1.x).
Download the current release, which is the GUI, or run the CLI from
PowerShell.

**"Incorrect passphrase or damaged file" but I'm sure it's right** — check
keyboard layout / Caps Lock; then verify file integrity: re-download or
compare hashes with the sender. `Lockjaw inspect <file>` confirms whether the
header is at least parseable.

**Very slow first step on old machines** — Argon2id intentionally uses
256 MiB of RAM and several seconds of compute per operation. This is the
brute-force protection working.
