using System;
using System.Linq;

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

        private string _name;
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

        private string _code;
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

        private void BaseCustomer_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { NameProperty, CodeProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(BaseCustomerObjectProperty);
            }
        }
    }
}
