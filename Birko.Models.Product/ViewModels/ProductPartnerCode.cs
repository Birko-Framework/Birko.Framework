using System;
using System.Linq;

namespace Birko.Models.Product.ViewModels
{
    public class ProductPartnerCode : Birko.Data.ViewModels.LogViewModel
    {
        public const string ProductGuidProperty = "ProductGuid";
        public const string PartnerNameProperty = "PartnerName";
        public const string CodeProperty = "Code";
        public const string ProductPartnerCodeObjectProperty = "ProductPartnerCode";

        public ProductPartnerCode()
        {
            PropertyChanged += ProductPartnerCode_PropertyChanged;
        }

        private Guid? _productGuid;
        public Guid? ProductGuid
        {
            get { return _productGuid; }
            set
            {
                if (_productGuid != value)
                {
                    _productGuid = value;
                    RaisePropertyChanged(ProductGuidProperty);
                }
            }
        }

        private string _partnerName = null!;
        public string PartnerName
        {
            get { return _partnerName; }
            set
            {
                if (_partnerName != value)
                {
                    _partnerName = value;
                    RaisePropertyChanged(PartnerNameProperty);
                }
            }
        }

        private string _code = null!;
        public string Code
        {
            get { return _code; }
            set
            {
                if (_code != value)
                {
                    _code = value;
                    RaisePropertyChanged(CodeProperty);
                }
            }
        }

        private void ProductPartnerCode_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { ProductGuidProperty, PartnerNameProperty, CodeProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(ProductPartnerCodeObjectProperty);
            }
        }
    }
}
