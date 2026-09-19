# Birko.Security.BCrypt

## Overview
BCrypt password hashing — pure C# Blowfish/BCrypt implementation. No external NuGet dependencies.

## Project Location
`Birko.Security.BCrypt/` — Shared project (.shproj + .projitems)

## Components
- **Hashing/BCryptPasswordHasher.cs** — Implements `IPasswordHasher`. BCrypt adaptive hashing with configurable work factor (4–31, default 12). Output format: `$2a$XX$` standard modular crypt. Includes `NeedsRehash()` for work factor upgrade detection. Full Blowfish implementation with S-boxes, P-array, EksBlowfish key schedule, BCrypt-specific Base64 encoding.

## Dependencies
- Birko.Security (IPasswordHasher interface)
- No external NuGet packages

## Maintenance
When modifying this project, update:
- This CLAUDE.md if components change
- README.md for API changes
- Root CLAUDE.md project listing
- Birko.Security.BCrypt.projitems if files are added/removed
