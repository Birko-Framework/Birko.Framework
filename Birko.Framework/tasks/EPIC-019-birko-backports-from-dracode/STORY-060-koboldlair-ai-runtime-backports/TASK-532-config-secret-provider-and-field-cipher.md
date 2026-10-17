---
id: TASK-532
parent: STORY-060
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-08
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Security: a configuration ISecretProvider and a field cipher keyed from a secret

## Context

`ISecretProvider` has only Azure Key Vault and Vault implementations, so nothing works for local, dev or plain
configuration. `AesEncryptionProvider` takes a raw key. DraCode wrote `Security/ConfigSecretProvider.cs` (48) and
`Security/ProviderKeyCipher.cs` (55) to encrypt provider API keys at rest. Fix rather than copy:

- the key is `SHA256(master)` — a hash, not a key-derivation function
- ciphertexts carry no key id or version, so rotating the master makes every stored value unreadable
- no associated data, so a ciphertext can be moved between rows

Adopted in the consumer by DraCode TASK-130 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`).

## Acceptance criteria

- [x] An `ISecretProvider` over `IConfiguration` (and/or in-memory) in `Birko.Security`
- [x] A field cipher: KDF-derived key, versioned envelope with a key id, caller-supplied associated data, AEAD (AES-GCM)
- [x] Rotation: decrypt under any known key id, encrypt under the current one
- [x] A missing master secret throws at construction, with no fallback value
- [x] Tests: round-trip, wrong associated data fails, tampering fails, rotation, missing secret

## Human test plan

N/A — library behaviour, covered by unit tests.

## Resolution (2026-10-17)

**`InMemorySecretProvider`** (`Birko.Security`, zero dependencies): DraCode's `ConfigSecretProvider` was an
in-memory dictionary seeded from configuration, so that is what ships. It is seeded from any key/value sequence:
`configuration.GetSection("Secrets").AsEnumerable(makePathsRelative: true)`, with null-valued sections skipped. It is
flat and case-sensitive, read-write, and lists full names by prefix. No `IConfiguration`-typed provider was added:
it would have given `Birko.Security` its first configuration package, and the existing configuration integration lives
in `Birko.Security.Vault.Configuration`, which needs Vault.

**`FieldCipher`** (`Birko.Security.Encryption`), against DraCode's three defects:
- Key: `HKDF-SHA256(master, info: "Birko.FieldCipher/v1/" + keyId)`, not `SHA256(master)`. A master under 32 bytes is
  refused, because HKDF stretches entropy and does not create it.
- Envelope: `bfc1:{keyId}:{base64url(nonce || ciphertext || tag)}`, AES-256-GCM.
- AEAD: the header and the caller's associated data are authenticated, so neither the key id nor the row can change.
- Rotation: encrypt under `CurrentKeyId`, decrypt under the envelope's id; `NeedsReencryption` / `Reencrypt`.
- `FromSecretProviderAsync(secrets, current, previous, prefix)` throws for any listed master that is missing, empty
  or not base64, with no fallback value.

**Tests:** 25 in `FieldCipherAndInMemorySecretTests`. They cover round-trip (3 inputs), randomized nonce, another
instance, wrong associated data, tampered nonce/ciphertext/tag, relabelled key id, non-envelopes (4), rotation, an
unknown key id, the same master under two ids, missing current, short master, the secret-provider factory (both keys,
missing current, missing previous, non-base64), the generated master, and the in-memory provider. Mutation: dropping
the associated data from the AAD fails the moved-row test. Regression: all 14 importers of `Birko.Security.projitems`
are green (Security 100, AspNetCore 113, Grants 184, Jwt 48, OAuth.Server 48, Vault 65, AzureKeyVault 42, BCrypt 38,
NFC 49, Health.Azure 16, REST.Server 7, SOAP 7, SSE 25, WebSocket 71).
