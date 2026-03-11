using System;
using Birko.Data.Repositories;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.SQL.Repositories
{
    /// <summary>
    /// TimescaleDB repository for CRUD operations with bulk support.
    /// Inherits from DataBaseRepository which uses DataBaseBulkStore for bulk operations.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public class TimescaleDBRepository<TViewModel, TModel>
        : DataBaseRepository<SQL.Connectors.TimescaleDBConnector, TViewModel, TModel>
        where TModel : Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel : Models.ILoadable<TModel>
    {
        /// <summary>
        /// Initializes a new instance of the TimescaleDBRepository class.
        /// </summary>
        public TimescaleDBRepository() : base()
        { }
    }
}
