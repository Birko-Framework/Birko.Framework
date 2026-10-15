# Birko.Health.Data.IndexManagement.Tests

Tests for `Birko.Health.Data.IndexManagement` and for `IndexDeclarationDriftDetector` (Birko.Data.Patterns), offline,
against an in-memory `IIndexManager`. The detector is measured against live MongoDB, RavenDB and Cosmos DB in those
providers' own test projects (TASK-635).

## Running

```sh
dotnet test tests/Birko.Health.Data.IndexManagement.Tests --nologo
```
