using Birko.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Birko.Data.Filters
{
    public class ModelByGuid<TModel> : IRepositoryFilter<TModel>
         where TModel : AbstractModel
    {
        public Guid Guid { get; set; }

        public ModelByGuid(Guid guid)
        {
            Guid = guid;
        }

        public virtual Expression<Func<TModel, bool>> Filter()
        {
            return (x) => x.Guid == Guid;
        }
    }
}
