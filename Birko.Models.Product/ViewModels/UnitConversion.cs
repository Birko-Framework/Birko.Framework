using System;
using System.Linq;

namespace Birko.Models.Product.ViewModels
{
    public class UnitConversion : Birko.Data.ViewModels.LogViewModel
    {
        public const string FromMeasureUnitGuidProperty = "FromMeasureUnitGuid";
        public const string ToMeasureUnitGuidProperty = "ToMeasureUnitGuid";
        public const string FactorProperty = "Factor";
        public const string ProductGuidProperty = "ProductGuid";
        public const string UnitConversionObjectProperty = "UnitConversion";

        public UnitConversion()
        {
            PropertyChanged += UnitConversion_PropertyChanged;
        }

        private Guid _fromMeasureUnitGuid;
        public Guid FromMeasureUnitGuid { get => _fromMeasureUnitGuid; set { if (_fromMeasureUnitGuid != value) { _fromMeasureUnitGuid = value; RaisePropertyChanged(FromMeasureUnitGuidProperty); } } }

        private Guid _toMeasureUnitGuid;
        public Guid ToMeasureUnitGuid { get => _toMeasureUnitGuid; set { if (_toMeasureUnitGuid != value) { _toMeasureUnitGuid = value; RaisePropertyChanged(ToMeasureUnitGuidProperty); } } }

        private decimal _factor;
        public decimal Factor { get => _factor; set { if (_factor != value) { _factor = value; RaisePropertyChanged(FactorProperty); } } }

        private Guid? _productGuid;
        public Guid? ProductGuid { get => _productGuid; set { if (_productGuid != value) { _productGuid = value; RaisePropertyChanged(ProductGuidProperty); } } }

        private void UnitConversion_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { FromMeasureUnitGuidProperty, ToMeasureUnitGuidProperty, FactorProperty, ProductGuidProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(UnitConversionObjectProperty);
            }
        }
    }
}
