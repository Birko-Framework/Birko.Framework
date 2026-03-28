using System;
using Birko.Data.Models;

namespace Birko.Models.Customers
{
    public enum PartnerType
    {
        Customer = 0,
        Supplier = 1,
        Both = 2
    }

    public enum LegalType
    {
        Company = 0,
        Person = 1
    }

    public enum CustomerStatus
    {
        Active = 0,
        Inactive = 1,
        Blocked = 2
    }

    public class Customer
        : BaseCustomer
        , Birko.Data.Models.ILoadable<ViewModels.Customer>
    {
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Website { get; set; }
        public Guid? PriceGroupGuid { get; set; }
        public PartnerType PartnerType { get; set; } = PartnerType.Customer;
        public LegalType LegalType { get; set; } = LegalType.Company;
        /// <summary>Business identification number (IČO).</summary>
        public string? TaxId { get; set; }
        /// <summary>VAT identification number (DIČ / IČ DPH).</summary>
        public string? VatId { get; set; }
        public CustomerStatus Status { get; set; } = CustomerStatus.Active;

        public virtual void LoadFrom(ViewModels.Customer data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                PriceGroupGuid = data.PriceGroup?.Guid;
            }
        }

        public virtual void LoadFrom(Birko.Models.Pricing.ViewModels.PriceGroup data)
        {
            if (data != null)
            {
                PriceGroupGuid = data.Guid;
            }
        }
    }
}
