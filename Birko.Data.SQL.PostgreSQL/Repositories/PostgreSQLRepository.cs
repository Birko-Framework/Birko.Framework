using System;
using Birko.Data.Repositories;
using Birko.Data.SQL.Repositories;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.SQL.Repositories
{
    /// <summary>
    /// PostgreSQL repository for CRUD operations with bulk support.
    /// Inherits from DataBaseRepository which uses DataBaseBulkStore for bulk operations via COPY command.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public abstract class PostgreSQLRepository<TViewModel, TModel>
        : DataBaseRepository<SQL.Connectors.PostgreSQLConnector, TViewModel, TModel>
        where TModel : Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel : Models.ILoadable<TModel>
    {
        /// <summary>
        /// Initializes a new instance of the PostgreSQLRepository class.
        /// </summary>
        public PostgreSQLRepository() : base()
        { }
    }
}
