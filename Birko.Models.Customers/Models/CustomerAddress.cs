using System;
using Birko.Data.Attributes;
using Birko.Data.Models;

namespace Birko.Models.Customers
{
    [Table("CustomerAddresses")]
    public class CustomerAddress
        : Address
        , IRelatedToCustomer
        , Birko.Data.Models.ILoadable<ViewModels.CustomerAddress>
    {
        public Guid? CustomerGuid { get; set; }

        public virtual void LoadFrom(ViewModels.CustomerAddress data)
        {
            LoadFrom((ViewModels.BaseCustomer)data);
        }

        public virtual void LoadFrom(ViewModels.BaseCustomer data)
        {
            if (data != null)
            {
                CustomerGuid = data.Guid;
            }
        }
    }
}
