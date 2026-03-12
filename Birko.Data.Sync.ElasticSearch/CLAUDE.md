# Birko.Data.Sync.ElasticSearch

## Overview
Elasticsearch-specific sync knowledge item implementation for the Birko.Data.Sync framework.

## Project Location
`C:\Source\Birko.Data.Sync.ElasticSearch\`

## Components

### Models
- `ElasticSyncKnowledgeItem` - Implements `ISyncKnowledgeItem` with NEST attributes (@Text, @Integer, @Keyword, @Date, @Boolean)
  - Static `GenerateId()` method for document ID generation

## Dependencies
- Birko.Data.Sync
- NEST / Elasticsearch.Net

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
