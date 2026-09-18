using System;
using System.Collections.Generic;
using Birko.Models.Pricing.ViewModels;

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

        private PriceGroup? _priceGroup;
        public PriceGroup? PriceGroup
        {
            get { return _priceGroup; }
            set
            {
                _priceGroup = value;
                RaisePropertyChanged(PriceGroupProperty);
            }
        }

        // CR-L306: cached watched-property set — avoids per-event array allocation + LINQ scan.
        private static readonly HashSet<string> _watchedProperties = new() { PriceGroupProperty };

        private void Customer_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null && _watchedProperties.Contains(e.PropertyName))
            {
                RaisePropertyChanged(CustomerObjectProperty);
            }
        }
    }
}
