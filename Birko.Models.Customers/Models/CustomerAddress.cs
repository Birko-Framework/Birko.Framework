using System;
using Birko.Data.Models;

namespace Birko.Models.Customers
{
    public class CustomerAddress
        : Address
        , IRelatedToCustomer
        , Birko.Data.Models.ILoadable<ViewModels.CustomerAddress>
    {
        public Guid? CustomerGuid { get; set; }

        public virtual void LoadFrom(ViewModels.CustomerAddress data)
        {
            if (data == null) return;

            // The customer link (CustomerGuid).
            LoadFrom((ViewModels.BaseCustomer)data);

            // The actual address fields live on the ViewModel's nested Address; without this the
            // inherited Street/City/ZIP/… stayed at their string.Empty defaults after a load.
            if (data.Address != null)
            {
                base.LoadFrom(data.Address);
            }
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
