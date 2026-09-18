namespace Birko.Telemetry;

/// <summary>
/// Standard telemetry names for the Birko framework.
/// </summary>
public static class BirkoTelemetryConventions
{
    /// <summary>Meter name used for all store metrics.</summary>
    public const string MeterName = "Birko.Data.Store";

    /// <summary>ActivitySource name used for distributed tracing.</summary>
    public const string ActivitySourceName = "Birko.Data.Store";

    /// <summary>Histogram metric tracking operation duration in milliseconds.</summary>
    public const string OperationDurationMetric = "birko.store.operation.duration";

    /// <summary>Counter metric tracking total operation count.</summary>
    public const string OperationCountMetric = "birko.store.operation.count";

    /// <summary>Counter metric tracking total error count.</summary>
    public const string OperationErrorMetric = "birko.store.operation.errors";

    /// <summary>Tag name for the store type (e.g., "JsonStore`1").</summary>
    public const string StoreTypeTag = "birko.store.type";

    /// <summary>Tag name for the entity type (e.g., "MyEntity").</summary>
    public const string EntityTypeTag = "birko.store.entity_type";

    /// <summary>Tag name for the operation (e.g., "Read", "Create").</summary>
    public const string OperationTag = "birko.store.operation";

    // CR-L381: a "birko.store.tenant" TenantTag constant was declared here but never emitted by any metric
    // or activity (the instrumentation wrappers have no tenant context to source it from), so it was removed
    // to avoid implying tenant tagging exists. Re-introduce it alongside actual tenant resolution if/when the
    // wrappers gain a tenant source.

    /// <summary>Tag name indicating whether the operation is bulk.</summary>
    public const string BulkTag = "birko.store.bulk";

    /// <summary>Default HTTP header name for correlation IDs.</summary>
    public const string DefaultCorrelationIdHeader = "X-Correlation-Id";
}
