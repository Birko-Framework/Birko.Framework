using System;
using System.Linq;

namespace Birko.Models.Customers.ViewModels
{
    public class CustomerBankAccount : Birko.Data.ViewModels.LogViewModel
    {
        public const string CustomerGuidProperty = "CustomerGuid";
        public const string AccountNumberProperty = "AccountNumber";
        public const string IbanProperty = "Iban";
        public const string SwiftProperty = "Swift";
        public const string BankNameProperty = "BankName";
        public const string CurrencyCodeProperty = "CurrencyCode";
        public const string IsDefaultProperty = "IsDefault";

        private Guid? _customerGuid;
        public Guid? CustomerGuid
        {
            get { return _customerGuid; }
            set { if (_customerGuid != value) { _customerGuid = value; RaisePropertyChanged(CustomerGuidProperty); } }
        }

        private string _accountNumber = string.Empty;
        public string AccountNumber
        {
            get { return _accountNumber; }
            set { if (_accountNumber != value) { _accountNumber = value; RaisePropertyChanged(AccountNumberProperty); } }
        }

        private string _iban = string.Empty;
        public string Iban
        {
            get { return _iban; }
            set { if (_iban != value) { _iban = value; RaisePropertyChanged(IbanProperty); } }
        }

        private string _swift = string.Empty;
        public string Swift
        {
            get { return _swift; }
            set { if (_swift != value) { _swift = value; RaisePropertyChanged(SwiftProperty); } }
        }

        private string _bankName = string.Empty;
        public string BankName
        {
            get { return _bankName; }
            set { if (_bankName != value) { _bankName = value; RaisePropertyChanged(BankNameProperty); } }
        }

        private string _currencyCode = string.Empty;
        public string CurrencyCode
        {
            get { return _currencyCode; }
            set { if (_currencyCode != value) { _currencyCode = value; RaisePropertyChanged(CurrencyCodeProperty); } }
        }

        private bool _isDefault;
        public bool IsDefault
        {
            get { return _isDefault; }
            set { if (_isDefault != value) { _isDefault = value; RaisePropertyChanged(IsDefaultProperty); } }
        }
    }
}
