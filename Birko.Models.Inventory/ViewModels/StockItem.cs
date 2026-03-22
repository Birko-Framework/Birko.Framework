using System;
using System.ComponentModel;
using System.Linq;

namespace Birko.Models.Inventory.ViewModels
{
    public class StockItem : Data.ViewModels.LogViewModel
    {
        public const string CodeProperty = "Code";
        public const string BarCodeProperty = "BarCode";
        public const string NameProperty = "Name";
        public const string ShortNameProperty = "ShortName";
        public const string DescriptionProperty = "Description";
        public const string TypeProperty = "Type";
        public const string StockItemObjectProperty = "StockItem";

        public StockItem()
        {
            PropertyChanged += StockItem_PropertyChanged;
        }

        private string _code = null!;
        public string Code { get => _code; set { if (_code != value) { _code = value; RaisePropertyChanged(CodeProperty); } } }

        private string _barCode = null!;
        public string BarCode { get => _barCode; set { if (_barCode != value) { _barCode = value; RaisePropertyChanged(BarCodeProperty); } } }

        private string _name = null!;
        public string Name { get => _name; set { if (_name != value) { _name = value; RaisePropertyChanged(NameProperty); } } }

        private string _shortName = null!;
        public string ShortName { get => _shortName; set { if (_shortName != value) { _shortName = value; RaisePropertyChanged(ShortNameProperty); } } }

        private string _description = null!;
        public string Description { get => _description; set { if (_description != value) { _description = value; RaisePropertyChanged(DescriptionProperty); } } }

        private string _type = null!;
        public string Type { get => _type; set { if (_type != value) { _type = value; RaisePropertyChanged(TypeProperty); } } }

        public Guid? CategoryGuid { get; set; }
        public Guid? MeasureUnitGuid { get; set; }
        public Guid TenantGuid { get; set; }

        private void StockItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (new[] { CodeProperty, BarCodeProperty, NameProperty, ShortNameProperty, DescriptionProperty, TypeProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(StockItemObjectProperty);
            }
        }
    }
}
