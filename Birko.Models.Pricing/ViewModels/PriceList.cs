using System;
using System.ComponentModel;

namespace Birko.Models.Pricing.ViewModels
{
    public class PriceList : Data.ViewModels.LogViewModel
    {
        public const string NameProperty = "Name";
        public const string PriceListObjectProperty = "PriceList";

        public PriceList()
        {
            PropertyChanged += PriceList_PropertyChanged;
        }

        private string _name = null!;
        public string Name { get => _name; set { if (_name != value) { _name = value; RaisePropertyChanged(NameProperty); } } }

        public Guid? CurrencyGuid { get; set; }
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public bool IsActive { get; set; } = true;

        private void PriceList_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == NameProperty)
            {
                RaisePropertyChanged(PriceListObjectProperty);
            }
        }
    }
}
