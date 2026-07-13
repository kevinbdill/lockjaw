# Lockjaw container format v1

All integers are unsigned little-endian. Offsets below apply to passphrase mode.

| Offset | Length | Field |
|---:|---:|---|
| 0 | 4 | ASCII magic `LOCKJAW` |
| 4 | 2 | format version (`1`) |
| 6 | 1 | mode (`1` = passphrase) |
| 7 | 4 | mode-header length (`29`) |
| 11 | 1 | KDF (`1` = Argon2id13) |
| 12 | 4 | Argon2id operation count |
| 16 | 4 | Argon2id memory bytes |
| 20 | 16 | random salt |
| 36 | 4 | plaintext chunk size (`1,048,576`) |
| 40 | 24 | libsodium secretstream header |
| 64 | variable | authenticated ciphertext chunks |

The complete 64-byte public prefix is supplied as associated data to every
secretstream chunk. Changing any parseable header field therefore causes the
same authentication failure as a wrong passphrase or modified ciphertext.

## Encrypted payload

The first encrypted plaintext byte identifies the payload encoding:

- `0`: PAX tar follows directly.
- `1`: a raw DEFLATE stream follows; decompression yields PAX tar.

Filenames, directory names, timestamps, sizes, compression selection, and file
contents are therefore encrypted. Public metadata still exposes that the file is
Lockjaw, format version, mode, KDF cost, chunk size, and approximate total size.

## Chunk framing

Each non-final plaintext chunk is exactly 1 MiB. Libsodium adds 17 bytes of
secretstream overhead. The final plaintext chunk is from 0 through 1 MiB and
carries `TAG_FINAL`.

There are no separate length fields. A reader consumes up to 1 MiB + 17 bytes:

- a full chunk tagged `MESSAGE` continues the stream;
- a short chunk must be tagged `FINAL`;
- a full chunk tagged `FINAL` must be at physical end-of-file;
- EOF without `FINAL`, an unexpected tag, or bytes after `FINAL` is failure.

This framing keeps the public format small while supporting a full-sized final
chunk and constant memory.

## Passphrase derivation

The UTF-8 passphrase and stored 16-byte salt are passed to libsodium
`crypto_pwhash` using Argon2id13. The resulting 32-byte key initializes
XChaCha20-Poly1305 secretstream.

Production defaults are 256 MiB and 3 operations. On a process constrained to
less than 1 GiB, encryption selects 64 MiB and 4 operations. Decryption always
uses the authenticated values stored in the header, subject to defensive limits
of 8 MiB–1 GiB and 1–10 operations.

Libsodium's high-level password-hashing API exposes operation and memory limits;
v1 does not serialize a fictitious caller-controlled Argon2 parallelism value.

## Armor

Armor is a transport encoding of the complete binary container:

```text
-----BEGIN LOCKJAW ENCRYPTED FILE-----
<Base64 in 64-character lines>
-----END LOCKJAW ENCRYPTED FILE-----
```

Armor adds no cryptographic behavior. After Base64 decoding, the bytes are the
same v1 binary container.

