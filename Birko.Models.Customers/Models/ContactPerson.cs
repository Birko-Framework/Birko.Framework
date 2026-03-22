using System;
using Birko.Data.SQL.Attributes;
using Birko.Data.Models;

namespace Birko.Models.Customers
{
    /// <summary>
    /// Contact person associated with a customer/partner.
    /// </summary>
    [Table("ContactPersons")]
    public class ContactPerson
        : AbstractDatabaseLogModel
        , IRelatedToCustomer
        , ILoadable<ViewModels.ContactPerson>
    {
        public Guid? CustomerGuid { get; set; }

        [PrecisionField(256)]
        public string Name { get; set; } = string.Empty;

        [PrecisionField(256)]
        public string Position { get; set; } = string.Empty;

        [PrecisionField(256)]
        public string Phone { get; set; } = string.Empty;

        [PrecisionField(256)]
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
