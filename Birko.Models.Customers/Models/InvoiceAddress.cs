using System;
using Birko.Data.Attributes;
using Birko.Data.Models;

namespace Birko.Models.Customers
{
    public interface IRelatedToInvoiceAddress : Birko.Data.Models.ILoadable<ViewModels.InvoiceAddress>
    {
        Guid? InvoiceAddressGuid { get; set; }
    }

    [Table("InvoiceAddresses")]
    public class InvoiceAddress
        : Address
        , Birko.Data.Models.ILoadable<ViewModels.InvoiceAddress>
        , ICopyable<InvoiceAddress>
    {
        public string BIN { get; set; } = string.Empty;
        public string TIN { get; set; } = string.Empty;
        public string VATIN { get; set; } = string.Empty;
        public string BankAccount { get; set; } = string.Empty;

        public virtual InvoiceAddress CopyTo(InvoiceAddress clone)
        {
            if (clone == null)
            {
                clone = new InvoiceAddress();
            }
            clone = (InvoiceAddress)base.CopyTo((Address)clone);
            clone.BIN = BIN;
            clone.TIN = TIN;
            clone.VATIN = VATIN;
            clone.BankAccount = BankAccount;

            return clone;
        }

        public override void LoadFrom(ViewModels.Address data)
        {
            base.LoadFrom(data);
            if (data is not ViewModels.InvoiceAddress invoiceData) return;
            BIN = invoiceData.BIN;
            TIN = invoiceData.TIN;
            VATIN = invoiceData.VATIN;
            BankAccount = invoiceData.BankAccount;
        }

        public virtual void LoadFrom(ViewModels.InvoiceAddress data)
        {
            LoadFrom((ViewModels.Address)data);
        }
    }
}
