# Birko.Data.SQL.Views

SQL platform implementation for Birko.Data.Views. Translates ViewDefinition into SQL Tables.View metadata and reuses existing connector infrastructure for query execution and DDL.

## Components

- **SqlViewTranslator** — Converts ViewDefinition → Tables.View by loading source tables via DataBase.LoadTable, mapping fields, creating FunctionFields for aggregates, and building Join conditions
- **SqlViewStore\<TView\>** — Implements IViewStore\<TView\> using existing connector SelectView/Select infrastructure
- **SqlViewManager** — Implements IViewManager using connector CreateView/DropView/ViewExists

## Dependencies
- Birko.Data.Views (ViewDefinition, IViewStore, IViewManager)
- Birko.Data.SQL (DataBase, AbstractConnector, Tables, Fields, Conditions)
- Birko.Data.SQL.View (Tables.View, ViewQueryMode, FunctionField)

## Namespace
`Birko.Data.SQL.Views`
