# Birko.Data.ElasticSearch.Views

ElasticSearch platform implementation for Birko.Data.Views. Translates ViewDefinition into NEST search requests with aggregations for grouped queries and _source filtering for simple queries.

## Components

- **ElasticSearchViewStore\<TView\>** — Implements IViewStore\<TView\> using NEST SearchRequest. Non-aggregate queries use _source filtering with sort/pagination. Aggregate queries use shared `StoreAggregationHelper` for metric creation (`BuildSingleMetricAggregation`), GROUP BY logic (`BuildGroupByAggregation`), and metric extraction (`ExtractMetricValues`). Joins are not supported in ES and are silently ignored.
- **ElasticSearchViewManager** — Implements IViewManager. EnsureAsync creates a destination index for persistent views (ES lacks native views; data population requires Transforms or reindex jobs). DropAsync/ExistsAsync/RefreshAsync map to index lifecycle operations.

## Dependencies
- Birko.Data.Views (ViewDefinition, IViewStore, IViewManager)
- Birko.Data.Stores (AggregateFunction, OrderByHelper)
- Birko.Data.ElasticSearch (ElasticSearch.ParseExpression, StoreAggregationHelper)
- NEST (ElasticClient, SearchRequest, aggregation types)

## Namespace
`Birko.Data.ElasticSearch.Views`
