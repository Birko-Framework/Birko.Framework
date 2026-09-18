using System;
using System.Linq;

namespace Birko.Models.Product.ViewModels
{
    public class MeasureUnit : Birko.Data.ViewModels.LogViewModel
    {
        public const string CodeProperty = "Code";
        public const string NameProperty = "Name";
        public const string SymbolProperty = "Symbol";
        public const string IsDefaultProperty = "IsDefault";
        public const string SortOrderProperty = "SortOrder";
        public const string MeasureUnitObjectProperty = "MeasureUnit";

        public MeasureUnit()
        {
            PropertyChanged += MeasureUnit_PropertyChanged;
        }

        private string _code = null!;
        public string Code { get => _code; set { if (_code != value) { _code = value; RaisePropertyChanged(CodeProperty); } } }

        private string _name = null!;
        public string Name { get => _name; set { if (_name != value) { _name = value; RaisePropertyChanged(NameProperty); } } }

        private string? _symbol;
        public string? Symbol { get => _symbol; set { if (_symbol != value) { _symbol = value; RaisePropertyChanged(SymbolProperty); } } }

        private bool _isDefault;
        public bool IsDefault { get => _isDefault; set { if (_isDefault != value) { _isDefault = value; RaisePropertyChanged(IsDefaultProperty); } } }

        private int _sortOrder;
        public int SortOrder { get => _sortOrder; set { if (_sortOrder != value) { _sortOrder = value; RaisePropertyChanged(SortOrderProperty); } } }

        private void MeasureUnit_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { CodeProperty, NameProperty, SymbolProperty, IsDefaultProperty, SortOrderProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(MeasureUnitObjectProperty);
            }
        }
    }
}
