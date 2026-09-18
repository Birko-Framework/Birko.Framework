using System.ComponentModel;

namespace Birko.Models.Pricing.ViewModels
{
    public class PriceGroup : Data.ViewModels.LogViewModel
    {
        public const string NameProperty = "Name";
        public const string PriceGroupObjectProperty = "PriceGroup";

        public PriceGroup()
        {
            PropertyChanged += PriceGroup_PropertyChanged;
        }

        private string _name = null!;
        public string Name { get => _name; set { if (_name != value) { _name = value; RaisePropertyChanged(NameProperty); } } }

        public decimal Percentage { get; set; }
        public bool IsDefault { get; set; }

        private void PriceGroup_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == NameProperty)
            {
                RaisePropertyChanged(PriceGroupObjectProperty);
            }
        }
    }
}
