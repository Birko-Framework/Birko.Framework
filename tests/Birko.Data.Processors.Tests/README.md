# Birko.Data.Processors.Tests

Unit tests for the Birko.Data.Processors stream processing framework.

## Test Coverage

- **CsvParserTests** — RFC 4180 parsing: simple rows, quoted fields, escaped quotes, custom delimiter, no trailing newline, empty stream, no enclosure, multiline quotes, line tracking
- **CsvProcessorTests** — CSV processor: header skip, sync/async, cancellation, custom delimiter, process finished event
- **XmlProcessorTests** — XML processor: element parsing, sync/async, CDATA, multiple items, cancellation, missing source file
- **ZipProcessorTests** — ZIP decorator: extract + process, sync/async, empty ZIP, invalid entry index, file cleanup
- **HttpProcessorTests** — HTTP decorator: dispose safety, inner access, filename sanitization, invalid URL error, event forwarding

## Running Tests

```bash
dotnet test Birko.Data.Processors.Tests/
```

## Test Framework

- xUnit 2.9.3
- FluentAssertions 7.0.0

## License

MIT License - see [License.md](License.md)
