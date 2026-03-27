# Birko.Data.ElasticSearch.Views

ElasticSearch platform implementation for Birko.Data.Views. Translates ViewDefinition into NEST search requests with aggregations for grouped queries and _source filtering for simple queries.

## Components

- **ElasticSearchViewStore\<TView\>** — Implements IViewStore\<TView\> using NEST SearchRequest. Non-aggregate queries use _source filtering with sort/pagination. Aggregate queries use TermsAggregation (single GROUP BY) or CompositeAggregation (multiple GROUP BY) with metric sub-aggregations (Sum, Avg, Min, Max, ValueCount). Joins are not supported in ES and are silently ignored.
- **ElasticSearchViewManager** — Implements IViewManager. EnsureAsync creates a destination index for persistent views (ES lacks native views; data population requires Transforms or reindex jobs). DropAsync/ExistsAsync/RefreshAsync map to index lifecycle operations.

## Dependencies
- Birko.Data.Views (ViewDefinition, IViewStore, IViewManager, AggregateFunction)
- Birko.Data.ElasticSearch (ElasticSearch.ParseExpression for filter translation)
- NEST (ElasticClient, SearchRequest, aggregation types)

## Namespace
`Birko.Data.ElasticSearch.Views`
