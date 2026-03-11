namespace Birko.Data.SQL.Repositories
{
    /// <summary>
    /// MySQL repository for direct model access with bulk support.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public class MySQLModelRepository<T>
        : Data.Repositories.DataBaseModelRepository<SQL.Connectors.MySQLConnector, T>
        where T : Models.AbstractModel
    {
        public MySQLModelRepository() : base()
        { }
    }
}
