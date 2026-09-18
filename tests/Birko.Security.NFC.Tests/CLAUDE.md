# Birko.Security.NFC.Tests

## Overview
Unit tests for Birko.Security.NFC authentication (auth provider, tag mappings, in-memory store).

## Project Location
`C:\Source\Birko.Security.NFC.Tests\`

## Components
- **NfcAuthProviderTests.cs** — Enroll (valid, duplicate, revoked re-enroll, max tags, empty UID), Authenticate (success, unknown, revoked, expired, expiration disabled), UID normalization (case, colons, dashes), usage tracking (enabled/disabled), Revoke (single, all, unknown), Query (active only, IsEnrolled, GetTagMapping)
- **NfcTagMappingTests.cs** — Default values, IsExpired logic (null, future, past)
- **NfcAuthResultTests.cs** — Success/Failure factory methods, token inclusion
- **NfcAuthSettingsTests.cs** — Default values verification
- **InMemoryNfcTagMappingStoreTests.cs** — Add/Get/Update/Delete CRUD, duplicate key rejection, user query, not-found handling

## Dependencies
- Birko.Security (ITokenProvider, TokenResult, TokenOptions)
- Birko.Security.NFC (code under test)
- xUnit 2.9.3, FluentAssertions 7.0.0

## Maintenance
When adding new auth features or stores, add corresponding tests here.
