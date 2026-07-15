# Birko.Data.XML.ViewModel.Tests

Test project for `Birko.Data.XML.ViewModel` (the XML-backed ViewModel repository wrappers).

## Scope
- `XmlRepositoryUnwrapTests` — CR-L247 (the project had no .Tests sibling). Covers the only behavior
  the two repositories add over the base `AbstractBulkViewModelRepository` /
  `AbstractAsyncBulkViewModelRepository`: the constructor store-type guard (accept a raw or
  tenant-wrapped `XmlStore`/`AsyncXmlStore`, reject a foreign store with `ArgumentException`, accept
  null leaving `Store` unset) and the `XmlStore` unwrap property (resolving the concrete store through
  a wrapper where a plain cast is null). Also pins CR-L246: the guard validates before the base assigns
  `Store` (private `ValidateStore` helper), replacing the misleading `base(null)` + conditional-assign.

## Conventions
- xUnit + FluentAssertions; the store-type/unwrap logic delegates to the well-tested
  `StoreExtensions.IsStoreOfType` / `GetUnwrappedStore`, so these are thin guard tests.
- Repos under test are private test subclasses supplying `MapToModel`; foreign/wrapping stores are
  minimal hand-rolled `IStore`/`IAsyncStore` doubles (mirrors the JSON.ViewModel.Tests sibling).
