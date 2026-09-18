# Birko.Communication.NFC.Tests

## Overview
Unit tests for Birko.Communication.NFC tag communication (models, protocols, transports).

## Project Location
`C:\Source\Birko.Communication.NFC.Tests\`

## Components
- **NfcTagDataTests.cs** — Tag data model: ToString, GetFormattedUid, default values
- **NdefRecordTests.cs** — NDEF record: URI extraction (http, https, tel, mailto prefixes), text extraction (UTF-8 + language), TypeString, edge cases (wrong TNF, empty payload)
- **NdefProtocolTests.cs** — NDEF message parsing: TLV wrapper, URI/text records, terminator, empty input, CanHandle tag types
- **Iso14443AProtocolTests.cs** — ISO 14443A: SAK classification (MIFARE Classic/Ultralight/DESFire/NTAG), CanHandle (supported/unsupported types), Parse metadata (ATQA, UID length/type), payload handling
- **HidNfcTransportTests.cs** — HID transport: connect/disconnect, FeedInputAsync with hex/colon-separated/decimal UIDs, timeout, partial input completion, polling TagDetected event, TransceiveAsync rejection
- **NfcReaderSettingsTests.cs** — Settings defaults, GetID format

## Dependencies
- Birko.Communication (AbstractPort, PortSettings)
- Birko.Communication.NFC (code under test)
- xUnit 2.9.3, FluentAssertions 7.0.0

## Maintenance
When adding new NFC transports or protocols, add corresponding tests here.
