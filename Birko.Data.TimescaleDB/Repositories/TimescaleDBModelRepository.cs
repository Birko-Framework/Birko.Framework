namespace Birko.Data.SQL.Repositories
{
    /// <summary>
    /// TimescaleDB repository for direct model access with bulk support.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public class TimescaleDBModelRepository<T>
        : DataBaseModelRepository<SQL.Connectors.TimescaleDBConnector, T>
        where T : Models.AbstractModel
    {
        public TimescaleDBModelRepository() : base()
        { }
    }
}
