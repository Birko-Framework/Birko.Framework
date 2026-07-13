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
        , ICopyable<Customer>
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

        // Without this override, CopyTo runs the inherited BaseCustomer.CopyTo, which allocates a
        // plain BaseCustomer (wrong runtime type) and copies only Name/Code — silently dropping every
        // Customer-specific field. Mirrors the Address / InvoiceAddress override pattern.
        public virtual Customer CopyTo(Customer clone)
        {
            if (clone == null)
            {
                clone = new Customer();
            }
            clone = (Customer)base.CopyTo((BaseCustomer)clone);
            clone.Email = Email;
            clone.Phone = Phone;
            clone.Website = Website;
            clone.PriceGroupGuid = PriceGroupGuid;
            clone.PartnerType = PartnerType;
            clone.LegalType = LegalType;
            clone.TaxId = TaxId;
            clone.VatId = VatId;
            clone.Status = Status;
            return clone;
        }

        /// <summary>
        /// Hydrates from the customer view model. CR-M217: this is an intentionally <b>partial</b>
        /// projection — the view model exposes only Name/Code (via <c>base.LoadFrom</c>) and the
        /// PriceGroup. Email/Phone/Website/TaxId/VatId/Status/PartnerType/LegalType are NOT editable
        /// through this view model and are preserved from the existing entity (set via the store /
        /// domain services, not the VM). Do not treat a VM→model load as a full round-trip.
        /// </summary>
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
