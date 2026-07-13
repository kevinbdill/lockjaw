# M1 design review and specification corrections

The development specification had a sound high-level direction: one modern
libsodium construction, compression before encryption, a versioned format, and
no published partial plaintext. M1 makes the following corrections explicit.

## Corrections implemented

1. **Authenticate the whole public prefix.** The draft said the header is AAD
   for chunk 0, but did not define its exact bytes. M1 uses the complete public
   prefix—including the secretstream header—as AAD for every chunk.
2. **Define chunk framing.** Secretstream authenticates messages but does not
   serialize message lengths. V1 now defines fixed non-final chunks and an
   end-of-file final chunk, including the full-sized-final-chunk case.
3. **Do not claim caller-selected Argon2 parallelism.** Libsodium
   `crypto_pwhash` exposes operation and memory limits, not a parallelism input.
   V1 stores only parameters the implementation actually controls.
4. **Use one authentication failure.** The draft assigned separate CLI exit
   codes to bad passphrases and corruption while also requiring them to be
   indistinguishable. M1 returns the same message and exit code for both.
5. **Stage beside the destination.** An atomic rename from `%TEMP%` is not
   guaranteed across volumes. Extraction stages under the destination parent,
   then uses a same-volume directory move.
6. **Do not promise physical secure deletion.** Modern filesystems, SSD wear
   leveling, snapshots, and backups make that promise unsupportable. M1 prevents
   publication of partial plaintext, uses delete-on-close staging for the packed
   payload, and cleans extraction staging on failure.
7. **Use a decryption compatibility vector.** Random salt and a random
   secretstream header make exact ciphertext generation intentionally
   nondeterministic. A frozen known ciphertext should test future decryption;
   encryption tests should assert round-trip and security properties.
8. **Treat the binding as part of the trusted base.** The older `Sodium.Core`
   project named in the draft now recommends alternative bindings. M1 pins the
   modern documented binding, but its current alpha status is a release blocker
   until the dependency is frozen and independently reviewed or replaced by a
   stable maintained binding.

## Deliberately deferred

- X25519 recipient mode, sealed file-key stanzas, and multi-recipient handling
- identity encryption, DPAPI integration, contacts, and key backup
- WPF GUI, drag/drop, progress reporting, and shell integration
- fuzz target, 10 GB stress run, Windows clean-machine packaging test
- release signing and export-control filing

These are not represented by insecure placeholders. The M1 file parser rejects
recipient mode until its format and key-handling code are implemented and
reviewed.

## Additional release questions

- Decide whether compressed authenticated payloads need an extraction-size or
  expansion-ratio policy for hostile but valid senders.
- Decide how to handle Windows ACLs, alternate data streams, sparse files, and
  reparse points. M1 preserves bytes and timestamps only and rejects links.
- Validate single-file native extraction behavior and offline firewall logs on
  each Windows architecture shipped.
- Replace the broad legal statement in the draft with a review tied to the
  actual repository, distribution country, release channel, and current rules.

