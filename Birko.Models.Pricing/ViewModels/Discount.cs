using System;
using System.ComponentModel;

namespace Birko.Models.Pricing.ViewModels
{
    public class Discount : Data.ViewModels.LogViewModel
    {
        public const string NameProperty = "Name";
        public const string DiscountObjectProperty = "Discount";

        public Discount()
        {
            PropertyChanged += Discount_PropertyChanged;
        }

        private string _name = null!;
        public string Name { get => _name; set { if (_name != value) { _name = value; RaisePropertyChanged(NameProperty); } } }

        public DiscountType Type { get; set; }
        public decimal Value { get; set; }
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public bool IsActive { get; set; } = true;

        private void Discount_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == NameProperty)
            {
                RaisePropertyChanged(DiscountObjectProperty);
            }
        }
    }
}
