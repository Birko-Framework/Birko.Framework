using System.ComponentModel;

namespace Birko.Models.Pricing.ViewModels
{
    public class Tax : Data.ViewModels.LogViewModel
    {
        public const string NameProperty = "Name";
        public const string ShortCutProperty = "ShortCut";
        public const string TaxObjectProperty = "Tax";

        public Tax()
        {
            PropertyChanged += Tax_PropertyChanged;
        }

        private string _name = null!;
        public string Name { get => _name; set { if (_name != value) { _name = value; RaisePropertyChanged(NameProperty); } } }

        private string _shortCut = null!;
        public string ShortCut { get => _shortCut; set { if (_shortCut != value) { _shortCut = value; RaisePropertyChanged(ShortCutProperty); } } }

        public decimal Percentage { get; set; }
        public bool Default { get; set; }

        private void Tax_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == NameProperty || e.PropertyName == ShortCutProperty)
            {
                RaisePropertyChanged(TaxObjectProperty);
            }
        }
    }
}
