using System;
using Birko.Data.SQL.Attributes;
using Birko.Data.Models;
using Birko.Models.Accounting;

namespace Birko.Models.Customers
{
    [Table("Customers")]
    public class Customer
        : BaseCustomer
        , Birko.Data.Models.ILoadable<ViewModels.Customer>
        , IRelatedToPriceGroup
    {
        public Guid? PriceGroupGuid { get; set; }

        public virtual void LoadFrom(ViewModels.Customer data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                PriceGroupGuid = data.PriceGroup?.Guid;
            }
        }

        public virtual void LoadFrom(Birko.Models.Accounting.ViewModels.PriceGroup data)
        {
            if (data != null)
            {
                PriceGroupGuid = data.Guid;
            }
        }
    }
}
