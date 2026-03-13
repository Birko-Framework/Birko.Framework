using System;
using Birko.Data.SQL.Attributes;
using Birko.Data.Models;

namespace Birko.Models.Customers
{
    public interface IRelatedToCustomer : Birko.Data.Models.ILoadable<ViewModels.BaseCustomer>
    {
        Guid? CustomerGuid { get; set; }
    }

    [Table("Customers")]
    public class BaseCustomer
        : Birko.Data.Models.AbstractDatabaseLogModel
        , Birko.Data.Models.ILoadable<ViewModels.BaseCustomer>
        , ICopyable<BaseCustomer>
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;

        public virtual BaseCustomer CopyTo(BaseCustomer clone)
        {
            if (clone == null)
            {
                clone = new BaseCustomer();
            }
            clone = (BaseCustomer)base.CopyTo(clone);
            clone.Name = Name;
            clone.Code = Code;
            return clone;
        }

        public virtual void LoadFrom(ViewModels.BaseCustomer data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            Name = data.Name;
            Code = data.Code;
        }
    }
}
