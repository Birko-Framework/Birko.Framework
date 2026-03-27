# Birko.Data.MongoDB.Views

MongoDB platform implementation for Birko.Data.Views. Translates ViewDefinition into aggregation pipeline stages and MongoDB views.

## Components

- **MongoViewTranslator** — Converts ViewDefinition → List\<BsonDocument\> pipeline stages ($lookup, $unwind, $group, $project)
- **MongoViewStore\<TView\>** — Implements IViewStore\<TView\> by executing aggregation pipelines. Supports Persistent (MongoDB view collection), OnTheFly (pipeline on source collection), and Auto modes
- **MongoViewManager** — Implements IViewManager using MongoDB `db.createView()` command (requires MongoDB 3.4+)

## Pipeline Translation

| ViewDefinition | MongoDB Stage |
|---|---|
| Join | $lookup + $unwind |
| GroupBy + Aggregates | $group |
| Select fields | $project |
| Filter (at query time) | $match |
| OrderBy | $sort |
| Limit/Offset | $limit / $skip |

## Dependencies
- Birko.Data.Views (ViewDefinition, IViewStore, IViewManager)
- Birko.Data.MongoDB (MongoDBClient)
- MongoDB.Driver, MongoDB.Bson

## Namespace
`Birko.Data.MongoDB.Views`
