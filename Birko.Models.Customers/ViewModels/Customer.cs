using System;
using System.Linq;
using Birko.Models.Accounting.ViewModels;

namespace Birko.Models.Customers.ViewModels
{
    public class Customer : BaseCustomer
    {
        public const string PriceGroupProperty = "PriceGroup";
        public const string CustomerObjectProperty = "Customer";

        public Customer()
        {
            PropertyChanged += Customer_PropertyChanged;
        }

        private PriceGroup _priceGroup;
        public PriceGroup PriceGroup
        {
            get { return _priceGroup; }
            set
            {
                _priceGroup = value;
                RaisePropertyChanged(PriceGroupProperty);
            }
        }

        private void Customer_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { PriceGroupProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(CustomerObjectProperty);
            }
        }
    }
}
