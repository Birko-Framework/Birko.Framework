# Birko.Security.BCrypt.Tests

## Overview
Unit tests for Birko.Security.BCrypt — BCrypt password hashing.

## Project Location
`C:\Source\Birko.Security.BCrypt.Tests\` — .csproj (net10.0, xUnit + FluentAssertions)

## Components
- **BCryptPasswordHasherTests.cs** — Hash/verify round-trip, work factor validation, output format, NeedsRehash, wrong password, unique salts

## Dependencies
- Birko.Security (.projitems)
- Birko.Security.BCrypt (.projitems)

## Maintenance
When adding new BCrypt features, add corresponding tests here.
