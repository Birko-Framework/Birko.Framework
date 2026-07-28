# Birko.Data.ElasticSearch.Views

ElasticSearch platform implementation for Birko.Data.Views. Translates ViewDefinition into NEST search requests with aggregations for grouped queries and _source filtering for simple queries.

## Components

- **ElasticSearchViewStore\<TView\>** — Implements IViewStore\<TView\> using NEST SearchRequest. Non-aggregate queries use _source filtering with sort/pagination. Aggregate queries use shared `StoreAggregationHelper` for metric creation (`BuildSingleMetricAggregation`), GROUP BY logic (`BuildGroupByAggregation`), and metric extraction (`ExtractMetricValues`). Joins are not supported in ES and are silently ignored.
- **ElasticSearchViewManager** — Implements IViewManager. EnsureAsync creates a destination index for persistent views (ES lacks native views; data population requires Transforms or reindex jobs). DropAsync/ExistsAsync/RefreshAsync map to index lifecycle operations.

## Filter translation

`BuildFilterQuery` delegates to `ElasticSearch.ParseFilterQuery` — the shared boundary helper that enforces
CR-H047 (a supplied filter that cannot be translated throws `NotSupportedException` instead of widening to
match-all, which a `null` NEST query would). This invariant used to be implemented here **only**; it now lives
in `Birko.Data.ElasticSearch` and covers the entity stores' 14 conversion sites too, so the two paths cannot
drift. A null filter still means "no filter" and yields `MatchAllQuery` here, which is this view store's own
deliberate contract for an unfiltered view.

## Dependencies
- Birko.Data.Views (ViewDefinition, IViewStore, IViewManager)
- Birko.Data.Stores (AggregateFunction, OrderByHelper)
- Birko.Data.ElasticSearch (ElasticSearch.ParseFilterQuery / ParseExpression, StoreAggregationHelper)
- NEST (ElasticClient, SearchRequest, aggregation types)

## Namespace
`Birko.Data.ElasticSearch.Views`
