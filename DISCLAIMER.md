# Terms of Use & Disclaimer

**By downloading, installing, or using Lockjaw, you accept these terms.** If
you do not accept them, do not use the software.

## No warranty

Lockjaw is provided **"AS IS," without warranty of any kind**, express or
implied, including but not limited to the warranties of merchantability,
fitness for a particular purpose, and noninfringement — as stated in the
[MIT License](LICENSE) that governs this software.

## Limitation of liability

To the maximum extent permitted by law, the authors, copyright holders, and
contributors of Lockjaw are **not liable for any claim, damages, or other
liability** arising from or in connection with the software or its use,
including but not limited to:

- **Lost or forgotten passphrases.** Lockjaw has no back door, no recovery
  mechanism, and no key escrow — by design. If you lose your passphrase,
  **your data is permanently unrecoverable, and no one can help you**, including
  the authors.
- **Compromised passphrases.** If someone obtains your passphrase — through
  theft, guessing a weak passphrase, malware, keyloggers, phishing, or any
  other means — they can decrypt your files. Protecting the passphrase is
  entirely your responsibility.
- **Data loss or corruption** from any cause, including software defects,
  hardware failure, storage failure, or interrupted operations.
- **Security failures of the environment**, including a compromised operating
  system, malware on the machine, or recovery of deleted plaintext originals
  from disk (deleting a file is not forensic erasure).
- Any indirect, incidental, special, or consequential damages of any kind.

## Your responsibilities

- **Keep independent backups.** Encryption is not backup. Do not let a
  `.lockjaw` file be the only copy of data you cannot afford to lose.
- **Choose a strong passphrase and protect it.** See the
  [User Guide](docs/USER_GUIDE.md) for guidance.
- **Verify your downloads** against the SHA-256 checksums published in the
  release notes. Builds obtained anywhere other than the official
  [GitHub releases page](https://github.com/kevinbdill/lockjaw/releases) are
  not ours and are not covered by anything here.
- **Test your workflow.** Encrypt and decrypt a test file before trusting
  the software with important data.

## Security status

Lockjaw is a development-stage project and **has not received an independent
security audit** (see [SECURITY.md](SECURITY.md)). Do not rely on it as the
sole protection for irreplaceable or high-stakes data.

## Lawful use and export

You are responsible for complying with the laws of your jurisdiction,
including any restrictions on the use, import, or export of encryption
software. The software is published from the United States under License
Exception ENC / the publicly-available provisions of the EAR (see
[compliance/BIS_NOTIFICATION.md](compliance/BIS_NOTIFICATION.md)).

## Not legal advice

Nothing in this repository is legal advice.
