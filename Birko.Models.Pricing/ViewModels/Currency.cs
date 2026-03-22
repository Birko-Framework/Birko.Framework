using System.ComponentModel;

namespace Birko.Models.Pricing.ViewModels
{
    public class Currency : Data.ViewModels.LogViewModel
    {
        public const string NameProperty = "Name";
        public const string SymbolProperty = "Symbol";
        public const string CurrencyObjectProperty = "Currency";

        public Currency()
        {
            PropertyChanged += Currency_PropertyChanged;
        }

        private string _name = null!;
        public string Name { get => _name; set { if (_name != value) { _name = value; RaisePropertyChanged(NameProperty); } } }

        private string _symbol = null!;
        public string Symbol { get => _symbol; set { if (_symbol != value) { _symbol = value; RaisePropertyChanged(SymbolProperty); } } }

        public decimal FromRate { get; set; }
        public decimal ToRate { get; set; }
        public bool Default { get; set; }

        private void Currency_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == NameProperty || e.PropertyName == SymbolProperty)
            {
                RaisePropertyChanged(CurrencyObjectProperty);
            }
        }
    }
}
