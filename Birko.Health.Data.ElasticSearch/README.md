# Birko.Health.Data.ElasticSearch

Health check for Elasticsearch **mapping drift**: an index whose stored mapping no longer matches what its store
declares, so queries silently return nothing.

## Features

- Runs `DetectMappingDriftAsync()` over the stores you list and reports drifted fields (`Missing`, `Unexpected`,
  `TypeMismatch`).
- **Degraded** on drift or on a store it could not ask; **Healthy** only for what it verified.

## Usage

```csharp
var check = new ElasticSearchMappingDriftHealthCheck(new Func<CancellationToken, Task<MappingDriftReport>>[]
{
    ct => orderStore.DetectMappingDriftAsync(ct),
    ct => customerStore.DetectMappingDriftAsync(ct),
});
var result = await check.CheckAsync();
```

## License

See the repository's root `LICENSE`.
