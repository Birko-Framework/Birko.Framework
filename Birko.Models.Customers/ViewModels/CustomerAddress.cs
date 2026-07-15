using System;
using System.Collections.Generic;

namespace Birko.Models.Customers.ViewModels
{
    public class CustomerAddress : BaseCustomer
    {
        public const string AddressProperty = "Address";
        public const string CustomerAddressObjectProperty = "CustomerAddress";

        public CustomerAddress()
        {
            PropertyChanged += CustomerAddress_PropertyChanged;
        }

        private Address? _address;
        public Address? Address
        {
            get { return _address; }
            set
            {
                _address = value;
                RaisePropertyChanged(AddressProperty);
            }
        }

        // CR-L306: cached watched-property set — avoids per-event array allocation + LINQ scan.
        private static readonly HashSet<string> _watchedProperties = new() { AddressProperty };

        private void CustomerAddress_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null && _watchedProperties.Contains(e.PropertyName))
            {
                RaisePropertyChanged(CustomerAddressObjectProperty);
            }
        }
    }
}
