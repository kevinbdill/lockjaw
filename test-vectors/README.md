# Test vectors

Lockjaw encryption is intentionally randomized twice: Argon2id receives a fresh
salt and libsodium secretstream creates a fresh 192-bit stream header. Therefore,
"known key + known plaintext = exact ciphertext" is not a valid encryption
invariant and the production code does not expose a deterministic RNG hook.

Before format v1 is frozen, generate and commit a known `.lockjaw` file with the
release candidate, then test that every future build can decrypt it to the known
plaintext. Keep randomized round-trip, header-tamper, ciphertext-tamper, and
truncation tests alongside that compatibility vector.

Do not weaken or replace libsodium randomness merely to make encryption output
byte-for-byte deterministic.

