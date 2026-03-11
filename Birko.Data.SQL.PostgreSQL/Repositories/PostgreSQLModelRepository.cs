namespace Birko.Data.SQL.Repositories
{
    /// <summary>
    /// PostgreSQL repository for direct model access with bulk support.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public class PostgreSQLModelRepository<T>
        : Data.Repositories.DataBaseModelRepository<SQL.Connectors.PostgreSQLConnector, T>
        where T : Models.AbstractModel
    {
        public PostgreSQLModelRepository() : base()
        { }
    }
}
