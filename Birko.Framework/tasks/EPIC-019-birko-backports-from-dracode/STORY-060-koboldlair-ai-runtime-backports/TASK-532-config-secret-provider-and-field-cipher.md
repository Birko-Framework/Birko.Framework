---
id: TASK-532
parent: STORY-060
feature: null
status: todo
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

- [ ] An `ISecretProvider` over `IConfiguration` (and/or in-memory) in `Birko.Security`
- [ ] A field cipher: KDF-derived key, versioned envelope with a key id, caller-supplied associated data, AEAD (AES-GCM)
- [ ] Rotation: decrypt under any known key id, encrypt under the current one
- [ ] A missing master secret throws at construction, with no fallback value
- [ ] Tests: round-trip, wrong associated data fails, tampering fails, rotation, missing secret

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan
