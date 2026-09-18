using System;
using System.Collections.Generic;

namespace Birko.Models.Customers.ViewModels
{
    public class InvoiceAddress : Address
    {
        public const string BINProperty = "BIN";
        public const string TINProperty = "TIN";
        public const string VATINProperty = "VATIN";
        public const string BankAccountProperty = "BankAccount";
        public const string InvoiceAddressObjectProperty = "InvoiceAddress";

        public InvoiceAddress()
        {
            PropertyChanged += InvoiceAddress_PropertyChanged;
        }

        private string _bin = string.Empty;
        public string BIN
        {
            get { return _bin; }
            set
            {
                if (_bin != value)
                {
                    _bin = value;
                    RaisePropertyChanged(BINProperty);
                }
            }
        }

        private string _tin = string.Empty;
        public string TIN
        {
            get { return _tin; }
            set
            {
                if (_tin != value)
                {
                    _tin = value;
                    RaisePropertyChanged(TINProperty);
                }
            }
        }

        private string _vatin = string.Empty;
        public string VATIN
        {
            get { return _vatin; }
            set
            {
                if (_vatin != value)
                {
                    _vatin = value;
                    RaisePropertyChanged(VATINProperty);
                }
            }
        }

        private string _bankAccount = string.Empty;
        public string BankAccount
        {
            get { return _bankAccount; }
            set
            {
                if (_bankAccount != value)
                {
                    _bankAccount = value;
                    RaisePropertyChanged(BankAccountProperty);
                }
            }
        }

        // CR-L306: cached watched-property set — avoids per-event array allocation + LINQ scan.
        private static readonly HashSet<string> _watchedProperties = new()
        {
            BINProperty, TINProperty, VATINProperty, BankAccountProperty
        };

        private void InvoiceAddress_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null && _watchedProperties.Contains(e.PropertyName))
            {
                RaisePropertyChanged(InvoiceAddressObjectProperty);
            }
        }
    }
}
