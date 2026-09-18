using System;
using System.Linq;

namespace Birko.Models.ViewModels
{
    public class Value : Birko.Data.ViewModels.LogViewModel
    {
        public const string PriceProperty = "Price";
        public const string PriceVATProperty = "PriceVAT";
        public const string VATProperty = "VAT";
        public const string ValueObjectProperty = "Value";

        public Value()
        {
            PropertyChanged += Value_PropertyChanged;
        }

        private decimal? _price;
        public decimal? Price
        {
            get { return _price; }
            set
            {
                if (_price != value)
                {
                    _price = value;
                    RaisePropertyChanged(PriceProperty);
                }
            }
        }

        private decimal? _priceVAT;
        public decimal? PriceVAT
        {
            get { return _priceVAT; }
            set
            {
                if (_priceVAT != value)
                {
                    _priceVAT = value;
                    RaisePropertyChanged(PriceVATProperty);
                }
            }
        }

        private decimal? _vat;
        public decimal? VAT
        {
            get { return _vat; }
            set
            {
                if (_vat != value)
                {
                    _vat = value;
                    RaisePropertyChanged(VATProperty);
                }
            }
        }

        private void Value_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { PriceProperty, PriceVATProperty, VATProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(ValueObjectProperty);
            }
        }
    }
}
