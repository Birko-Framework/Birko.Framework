# Birko.Data.Core

## Overview
Core data layer foundation for the Birko Framework. Contains base models, view models, filters, and exceptions that all data projects depend on.

## Project Location
`C:\Source\Birko.Data.Core\`

## Components

### Models (`Birko.Data.Models`)
- **AbstractModel** — Base class for all data entities (Guid property), implements ICopyable and ILoadable<ModelViewModel>
- **ITimestamped** — Interface for entities with CreatedAt, UpdatedAt, PrevUpdatedAt timestamps
- **AbstractLogModel** — Extends AbstractModel with audit timestamps, implements ITimestamped
- **ICopyable\<T\>** — Interface for cloning objects
- **IDefault** — Interface for marking default items (bool Default)
- **ILoadable\<T\>** — Generic interface for loading/mapping from another object

### ViewModels (`Birko.Data.ViewModels`)
- **ViewModel** — Base class with INotifyPropertyChanged support
- **ModelViewModel** — Extends ViewModel with Guid, implements ILoadable<AbstractModel>
- **LogViewModel** — Extends ModelViewModel with audit timestamps
- **AbstractLogViewModel** — Extends ViewModel with audit timestamps (no Guid)

### Filters (`Birko.Data.Filters`)
- **IFilter\<TModel\>** — Generic filter interface returning Expression<Func<TModel, bool>>
- **ModelByGuid\<TModel\>** — Filter for single GUID lookup
- **ModelsByGuid\<TModel\>** — Filter for multiple GUID lookup

### Exceptions (`Birko.Data.Exceptions`)
- **StoreException** — Custom exception for store operations

## Dependencies
- None (standalone foundation project)

## Notes
- Models and ViewModels have a circular dependency via ILoadable (AbstractModel ↔ ModelViewModel). This is by design — see Technical Debt in TODO.md.
- All namespaces remain `Birko.Data.*` for backward compatibility.

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.

### Test Requirements
Every new public functionality must have corresponding unit tests.
