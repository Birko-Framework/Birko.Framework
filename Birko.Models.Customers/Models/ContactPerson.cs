using System;
using Birko.Data.Models;

namespace Birko.Models.Customers
{
    /// <summary>
    /// Contact person associated with a customer/partner.
    /// </summary>
    public class ContactPerson
        : AbstractLogModel
        , IRelatedToCustomer
        , ILoadable<ViewModels.ContactPerson>
        , Birko.Models.Contracts.IContactable
    {
        public Guid? CustomerGuid { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Position { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public bool IsPrimary { get; set; }

        public virtual void LoadFrom(ViewModels.ContactPerson data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            CustomerGuid = data.CustomerGuid;
            Name = data.Name;
            Position = data.Position;
            Phone = data.Phone;
            Email = data.Email;
            IsPrimary = data.IsPrimary;
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
