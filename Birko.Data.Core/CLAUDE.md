# Birko.Data.Core

## Overview
Core data layer foundation for the Birko Framework. Contains base models, view models, filters, and exceptions. Imports Birko.Contracts for shared interfaces (IGuidEntity, ILogEntity, ILoadable, ICopyable, ITimestamped, IDefault).

## Project Location
`C:\Source\Birko.Data.Core\` (shared project, .shproj/.projitems)

## Namespace
`Birko.Data` with sub-namespaces: `Birko.Data.Models`, `Birko.Data.ViewModels`, `Birko.Data.Filters`, `Birko.Data.Exceptions`.

## Components

### Models (`Birko.Data.Models`)
- **AbstractModel** — Base entity with Guid property, implements `IGuidEntity`, `ICopyable<AbstractModel>`, `ILoadable<IGuidEntity>`
- **AbstractLogModel** — Extends AbstractModel with CreatedAt/UpdatedAt/PrevUpdatedAt timestamps, implements `ITimestamped`, `ILoadable<ILogEntity>`

### ViewModels (`Birko.Data.ViewModels`)
- **ViewModel** — Base class with INotifyPropertyChanged support
- **ModelViewModel** — Extends ViewModel with Guid, implements `IGuidEntity`, `ILoadable<IGuidEntity>`, `ILoadable<ModelViewModel>`
- **LogViewModel** — Extends ModelViewModel with audit timestamps, implements `ILogEntity`, `ILoadable<ILogEntity>`, `ILoadable<LogViewModel>`
- **AbstractLogViewModel** — Extends ViewModel directly (no Guid/ModelViewModel), implements `ILogEntity`

### Filters (`Birko.Data.Filters`)
- **IFilter\<TModel\>** — Generic filter interface returning `Expression<Func<TModel, bool>>`
- **ModelByGuid\<TModel\>** — Filter for single GUID lookup
- **ModelsByGuid\<TModel\>** — Filter for multiple GUID lookup

### Exceptions (`Birko.Data.Exceptions`)
- **StoreException** — Custom exception for store operations

## Dependencies
- **Birko.Contracts** — Imported via .projitems. Provides `IGuidEntity`, `ILogEntity`, `ITimestamped`, `ILoadable<T>`, `ICopyable<T>`, `IDefault`.

## Key Design
Models reference `IGuidEntity`/`ILogEntity` interfaces (from Birko.Contracts), NOT ViewModel types. ViewModels reference Model types. This breaks the circular dependency that previously existed between Models and ViewModels via ILoadable.

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.

### Test Requirements
Every new public functionality must have corresponding unit tests.
