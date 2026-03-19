# Birko.Communication.NFC.Tests

Unit tests for the Birko.Communication.NFC tag communication library.

## Test Coverage

- **NfcTagDataTests** — ToString, GetFormattedUid, default values
- **NdefRecordTests** — URI extraction (all prefix types), text extraction (UTF-8), TypeString, edge cases
- **NdefProtocolTests** — NDEF message parsing (TLV wrapper, URI/text records, terminator), CanHandle tag types
- **Iso14443AProtocolTests** — SAK classification (all known types), CanHandle, Parse metadata (ATQA, UID type)
- **HidNfcTransportTests** — Connect/disconnect, FeedInputAsync, hex/decimal UID parsing, polling events, partial input
- **NfcReaderSettingsTests** — Default values, GetID format

## Test Framework

- xUnit 2.9.3
- FluentAssertions 7.0.0
- .NET 10.0

## Running Tests

```bash
dotnet test Birko.Communication.NFC.Tests
```

## License

See [License.md](License.md) for details.
