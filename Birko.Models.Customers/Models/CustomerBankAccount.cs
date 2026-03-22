using System;
using Birko.Data.Models;

namespace Birko.Models.Customers
{
    /// <summary>
    /// Bank account associated with a customer/partner.
    /// </summary>
    public class CustomerBankAccount
        : AbstractLogModel
        , IRelatedToCustomer
        , ILoadable<ViewModels.CustomerBankAccount>
    {
        public Guid? CustomerGuid { get; set; }

        public string AccountNumber { get; set; } = string.Empty;

        public string Iban { get; set; } = string.Empty;

        public string Swift { get; set; } = string.Empty;

        public string BankName { get; set; } = string.Empty;

        public string CurrencyCode { get; set; } = string.Empty;

        public bool IsDefault { get; set; }

        public virtual void LoadFrom(ViewModels.CustomerBankAccount data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            CustomerGuid = data.CustomerGuid;
            AccountNumber = data.AccountNumber;
            Iban = data.Iban;
            Swift = data.Swift;
            BankName = data.BankName;
            CurrencyCode = data.CurrencyCode;
            IsDefault = data.IsDefault;
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
