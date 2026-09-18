# Birko.Data.XML.ViewModel

## Overview
XML-based repository layer for ViewModel-to-Model mapping. Provides sync and async repository base classes that pair with `XmlStore`/`AsyncXmlStore` from Birko.Data.XML, supporting bulk operations and store wrapping (e.g., tenant wrappers).

## Project Location
`C:\Source\Birko.Data.XML.ViewModel\` — Shared project (.shproj + .projitems)

## Components
- **Repositories/XmlRepository.cs** — Abstract sync repository with bulk support. Validates that the injected store is `XmlStore<TModel>` or a wrapper around it. Exposes `XmlStore` property via `GetUnwrappedStore`.
- **Repositories/AsyncXmlRepository.cs** — Abstract async repository with bulk support. Validates that the injected store is `AsyncXmlStore<TModel>` or a wrapper around it. Exposes `XmlStore` property via `GetUnwrappedStore`.

## Dependencies
- Birko.Data.XML.Stores (`XmlStore<T>`, `AsyncXmlStore<T>`)
- Birko.Data.Repositories (`AbstractBulkViewModelRepository`, `AbstractAsyncBulkViewModelRepository`)
- Birko.Data.Stores (`IStore<T>`, `IAsyncStore<T>`, store type checking extensions)
- Birko.Data.Core (ViewModel/Model interfaces: `ILoadable<T>`, `AbstractModel`)
- Birko.Configuration (Settings)

## Maintenance
When modifying this project, update this CLAUDE.md, README.md, and root CLAUDE.md.
