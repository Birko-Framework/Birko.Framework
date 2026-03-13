using System;
using System.Linq;

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

        private void CustomerAddress_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { AddressProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(CustomerAddressObjectProperty);
            }
        }
    }
}
