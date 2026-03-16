# Birko.Data.Processors.Tests

## Overview
Unit tests for the Birko.Data.Processors stream processing framework.

## Project Location
`C:\Source\Birko.Data.Processors.Tests\` (xUnit test project, .csproj)

## Test Classes
- **CsvParserTests** — RFC 4180 parser: simple rows, quoted fields, escaped quotes, custom delimiter, no trailing newline, empty stream, no enclosure, multiline quotes, line tracking
- **CsvProcessorTests** — CSV processor: header skip, sync/async, cancellation, custom delimiter, process finished event
- **XmlProcessorTests** — XML processor: element parsing, sync/async, CDATA, multiple items, cancellation, missing source file
- **ZipProcessorTests** — ZIP decorator: extract + process, sync/async, empty ZIP, invalid entry index, file cleanup
- **HttpProcessorTests** — HTTP decorator: dispose safety, inner access, filename sanitization, invalid URL error, event forwarding

## Test Helpers
- **TestCsvProcessor** — Subclass exposing `CurrentItem` for test verification
- **TestXmlProcessor** — Subclass exposing `CurrentItem` for test verification
- **TestItem / XmlTestItem** — Simple POCOs for test data

## Dependencies
- Birko.Data.Processors (shared project import)
- Microsoft.Extensions.Logging.Abstractions
- xUnit 2.9.3, FluentAssertions 7.0.0

## Running Tests
```bash
dotnet test Birko.Data.Processors.Tests/
```
