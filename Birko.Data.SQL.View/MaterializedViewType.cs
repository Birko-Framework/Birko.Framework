namespace Birko.Data.SQL
{
    /// <summary>
    /// Specifies the type of materialized/indexed view support available for a database provider.
    /// </summary>
    public enum MaterializedViewType
    {
        /// <summary>
        /// No materialized or indexed view support.
        /// </summary>
        None = 0,

        /// <summary>
        /// PostgreSQL materialized views (CREATE MATERIALIZED VIEW).
        /// Stores query results physically; must be refreshed manually via REFRESH MATERIALIZED VIEW.
        /// </summary>
        PostgreSqlMaterialized = 1,

        /// <summary>
        /// SQL Server indexed views (CREATE VIEW ... WITH SCHEMABINDING + clustered index).
        /// Persists computed results; automatically maintained by the engine on underlying data changes.
        /// </summary>
        MSSqlIndexed = 2
    }
}
