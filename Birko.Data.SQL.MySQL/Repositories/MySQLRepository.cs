using System;
using Birko.Data.Repositories;
using Birko.Data.SQL.Repositories;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.SQL.Repositories
{
    /// <summary>
    /// MySQL repository for CRUD operations with bulk support.
    /// Inherits from DataBaseRepository which uses DataBaseBulkStore for bulk operations via LOAD DATA INFILE.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public class MySQLRepository<TViewModel, TModel>
        : DataBaseRepository<SQL.Connectors.MySQLConnector, TViewModel, TModel>
        where TModel : Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel : Models.ILoadable<TModel>
    {
        /// <summary>
        /// Initializes a new instance of the MySQLRepository class.
        /// </summary>
        public MySQLRepository() : base()
        { }
    }
}
