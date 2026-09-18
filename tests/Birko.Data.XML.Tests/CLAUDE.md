# Birko.Data.XML.Tests

Unit tests for Birko.Data.XML store implementations.

## Purpose

Provides comprehensive test coverage for XML file-based data stores, ensuring correctness and reliability of XML persistence operations.

## Test Structure

### XmlStoreTests
Basic store functionality tests:
- Constructor validation
- Default behavior (no initialization)
- Edge cases (empty GUID, null operations)
- Count operations

## Test Principles

- **Isolation**: Each test is independent
- **Fast**: Tests should run quickly
- **Clear**: Test names describe what they test
- **Minimal**: Test only what's necessary

## Dependencies

- Birko.Data.XML (system under test)
- Birko.* (framework dependencies)
- xUnit (test framework)
- FluentAssertions (assertions)
- Moq (mocking)
