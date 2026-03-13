namespace Birko.Data.SQL.Repositories
{
    /// <summary>
    /// MSSQL repository for direct model access with bulk support.
    /// </summary>
    /// <typeparam name="T">The type of data model.</typeparam>
    public class MSSqlModelRepository<T>
        : DataBaseModelRepository<SQL.Connectors.MSSqlConnector, T>
        where T : Models.AbstractModel
    {
        public MSSqlModelRepository() : base()
        { }
    }
}
