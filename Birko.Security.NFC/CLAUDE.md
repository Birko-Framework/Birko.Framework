# Birko.Security.NFC

## Overview
NFC-based authentication — maps NFC tag UIDs to user identities and optionally issues JWT tokens.

## Project Location
`Birko.Security.NFC/`

## Components
- **INfcAuthProvider.cs** — Interface: AuthenticateAsync, EnrollAsync, RevokeAsync, RevokeAllAsync, GetUserTagsAsync, GetTagMappingAsync, IsEnrolledAsync
- **NfcAuthProvider.cs** — Default implementation with INfcTagMappingStore + optional ITokenProvider. UID normalization, expiration enforcement, usage tracking, max tags per user
- **INfcTagMappingStore** — Persistence interface (defined in NfcAuthProvider.cs): GetByTagUidAsync, GetByUserIdAsync, AddAsync, UpdateAsync, DeleteAsync
- **InMemoryNfcTagMappingStore** — ConcurrentDictionary-based in-memory store (defined in NfcAuthProvider.cs)
- **NfcTagMapping.cs** — Model: Id, TagUid, UserId, UserName, Email, IsActive, EnrolledAt, LastUsedAt, Label, ExpiresAt
- **NfcAuthResult.cs** — Auth result: IsAuthenticated, UserId, UserName, Email, Token (TokenResult), Error, Claims, TagUid, Timestamp
- **NfcAuthSettings.cs** — Config: IssueTokens, TrackUsage, EnforceExpiration, MaxTagsPerUser, NormalizeUids

## Dependencies
- Birko.Security (ITokenProvider, TokenResult, TokenOptions)

## Maintenance
- When adding database-backed stores, create separate projects (e.g., Birko.Security.NFC.SQL) implementing INfcTagMappingStore
- Update README.md with new store documentation
- Update this CLAUDE.md with new components
