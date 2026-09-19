# Birko.Data.RavenDB.Tests

## Overview
Unit tests for Birko.Data.RavenDB — index management, assembly deployment, and Map/Reduce query helpers.

## Project Location
`tests/Birko.Data.RavenDB.Tests/`

## Test Framework
xUnit + FluentAssertions + Moq

## Dependencies
- Birko.Data.RavenDB (via .projitems import)
- Birko.Data.Patterns (IndexManagement interfaces)

## Structure
- `IndexManagement/` — Tests for RavenDBIndexManager (validation, assembly scanning, query helpers)
