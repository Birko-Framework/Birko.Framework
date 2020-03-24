using System;
using System.Collections.Generic;
using System.Text;
using Birko.Data.Repositories;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.SQL.Repositories
{
    public class MSSqlRepository<TViewModel, TModel> : DataBaseRepository<MSSqlConnector, TViewModel, TModel>
        where TModel : Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel : Models.ILoadable<TModel>
    {
        public MSSqlRepository() : base()
        { }
    }
}
