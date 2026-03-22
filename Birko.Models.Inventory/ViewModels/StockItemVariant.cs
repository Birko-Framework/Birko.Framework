using System;
using System.ComponentModel;

namespace Birko.Models.Inventory.ViewModels
{
    public class StockItemVariant : Data.ViewModels.LogViewModel
    {
        public const string NameProperty = "Name";
        public const string StockItemVariantObjectProperty = "StockItemVariant";

        public StockItemVariant()
        {
            PropertyChanged += StockItemVariant_PropertyChanged;
        }

        private string _name = null!;
        public string Name { get => _name; set { if (_name != value) { _name = value; RaisePropertyChanged(NameProperty); } } }

        public Guid? StockItemGuid { get; set; }

        private void StockItemVariant_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == NameProperty)
            {
                RaisePropertyChanged(StockItemVariantObjectProperty);
            }
        }
    }
}
