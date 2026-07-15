using System;
using System.Collections.Generic;

namespace Birko.Models.Customers.ViewModels
{
    public class BaseCustomer : Birko.Data.ViewModels.LogViewModel
    {
        public const string NameProperty = "Name";
        public const string CodeProperty = "Code";
        public const string BaseCustomerObjectProperty = "BaseCustomer";

        public BaseCustomer()
        {
            PropertyChanged += BaseCustomer_PropertyChanged;
        }

        private string _name = string.Empty;
        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    RaisePropertyChanged(NameProperty);
                }
            }
        }

        private string _code = string.Empty;
        public string Code
        {
            get { return _code; }
            set
            {
                if (_code != value)
                {
                    _code = value;
                    RaisePropertyChanged(CodeProperty);
                }
            }
        }

        // CR-L306: cached watched-property set — avoids per-event array allocation + LINQ scan.
        private static readonly HashSet<string> _watchedProperties = new() { NameProperty, CodeProperty };

        private void BaseCustomer_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null && _watchedProperties.Contains(e.PropertyName))
            {
                RaisePropertyChanged(BaseCustomerObjectProperty);
            }
        }
    }
}
