using Birko.Data.Repositories;
using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.SQL.Repositories
{
    public interface IDataBaseRepository<TConnector, TViewModel, TModel> : IViewModelRepository<TViewModel, TModel>
        where TConnector : SQL.Connectors.AbstractConnector
        where TModel : Models.AbstractModel
        where TViewModel : Models.ILoadable<TModel>
    {
        TConnector? Connector { get; }
        void AddOnInit(SQL.Connectors.InitConnector onInit);
        void RemoveOnInit(SQL.Connectors.InitConnector onInit);
    }
}
