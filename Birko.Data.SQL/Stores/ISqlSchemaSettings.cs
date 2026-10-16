namespace Birko.Data.SQL.Stores
{
    /// <summary>
    /// Settings that carry a connection-wide default schema (TASK-272).
    /// </summary>
    /// <remarks>
    /// An entity's own <c>[Table(Schema = …)]</c> or <c>ModelMap.ToTable(name, schema)</c> overrides it. On PostgreSQL,
    /// SQL Server and TimescaleDB a table without its own schema lives in this one; on MySQL and SQLite it is the
    /// <c>schema__</c> prefix of the table's name. Null keeps the connection's own default (<c>public</c>, <c>dbo</c>, the
    /// database). Part of the settings id, so two settings that differ only by schema never share a cached connector.
    /// </remarks>
    public interface ISqlSchemaSettings
    {
        string? Schema { get; }
    }
}
