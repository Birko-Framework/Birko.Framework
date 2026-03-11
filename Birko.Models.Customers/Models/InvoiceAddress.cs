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
        public string BIN { get; set; }
        public string TIN { get; set; }
        public string VATIN { get; set; }
        public string BankAccount { get; set; }

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
            if (data != null && data is ViewModels.InvoiceAddress invoiceData)
            {
                BIN = invoiceData.BIN;
                TIN = invoiceData.TIN;
                VATIN = invoiceData.VATIN;
                BankAccount = invoiceData.BankAccount;
            }
        }

        public virtual void LoadFrom(ViewModels.InvoiceAddress data)
        {
            LoadFrom((ViewModels.Address)data);
        }
    }
}
