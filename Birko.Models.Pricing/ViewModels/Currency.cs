using System.ComponentModel;

namespace Birko.Models.Pricing.ViewModels
{
    public class Currency : Data.ViewModels.LogViewModel
    {
        public const string CodeProperty = "Code";
        public const string NameProperty = "Name";
        public const string SymbolProperty = "Symbol";
        public const string IsLeftSymbolProperty = "IsLeftSymbol";
        public const string CurrencyObjectProperty = "Currency";

        public Currency()
        {
            PropertyChanged += Currency_PropertyChanged;
        }

        private string _code = null!;
        public string Code { get => _code; set { if (_code != value) { _code = value; RaisePropertyChanged(CodeProperty); } } }

        private string _name = null!;
        public string Name { get => _name; set { if (_name != value) { _name = value; RaisePropertyChanged(NameProperty); } } }

        private string _symbol = null!;
        public string Symbol { get => _symbol; set { if (_symbol != value) { _symbol = value; RaisePropertyChanged(SymbolProperty); } } }

        private bool _isLeftSymbol;
        public bool IsLeftSymbol { get => _isLeftSymbol; set { if (_isLeftSymbol != value) { _isLeftSymbol = value; RaisePropertyChanged(IsLeftSymbolProperty); } } }

        public bool IsDefault { get; set; }

        private void Currency_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == CodeProperty || e.PropertyName == NameProperty || e.PropertyName == SymbolProperty || e.PropertyName == IsLeftSymbolProperty)
            {
                RaisePropertyChanged(CurrencyObjectProperty);
            }
        }
    }
}
