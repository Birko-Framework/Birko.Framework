using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace Birko.ViewModels
{
    public class Product : Data.ViewModels.LogViewModel, Data.Models.ILoadable<Models.Product>, Birko.Data.Models.ILoadable<Product>
    {
        public const string SKUCodeProperty = "SKUCode";
        public const string BarCodeProperty = "BarCode";
        public const string NameProperty = "Name";
        public const string SlugProperty = "Slug";
        public const string DescriptionProperty = "Description";
        public const string CategoryProperty = "Category";
        public const string ProductObjectProperty = "ProductObject";

        public Product()
        {
            PropertyChanged += Product_PropertyChanged;
        }

        private string _SKUCode;
        public string SKUCode
        {
            get { return _SKUCode; }
            set
            {
                if (_SKUCode != value)
                {
                    _SKUCode = value;
                    RaisePropertyChanged(SKUCodeProperty);
                }
            }
        }

        private string _barCode;
        public string BarCode
        {
            get { return _barCode; }
            set
            {
                if (_barCode != value)
                {
                    _barCode = value;
                    RaisePropertyChanged(BarCodeProperty);
                }
            }
        }

        private string _name;
        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    RaisePropertyChanged(NameProperty);
                }
            }
        }

        private string _slug;
        public string Slug
        {
            get { return _slug; }
            set
            {
                if (_slug != value)
                {
                    _slug = value;
                    RaisePropertyChanged(SlugProperty);
                }
            }
        }

        private string _description;
        public string Description
        {
            get { return _description; }
            set
            {
                if (_description != value)
                {
                    _description = value;
                    RaisePropertyChanged(DescriptionProperty);
                }
            }
        }

        private string _category;
        public string Category
        {
            get { return _category; }
            set
            {
                if (_category != value)
                {
                    _category = value;
                    RaisePropertyChanged(CategoryProperty);
                }
            }
        }

        private void Product_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] {
                    SKUCodeProperty,
                    BarCodeProperty,
                    NameProperty,
                    DescriptionProperty,
                    CategoryProperty,
                    SlugProperty,
                }.Contains(e.PropertyName)
            )
            {
                RaisePropertyChanged(ProductObjectProperty);
            }
        }

        public void LoadFrom(Models.Product data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                SKUCode = data.SKUCode;
                BarCode = data.BarCode;
                Name = data.Name;
                Slug = data.Slug;
                Description = data.Description;
                Category = data.Category;
                if (this is IProductManufacturer pm && data is Models.IProductManufacturer dm)
                {
                    pm.LoadFrom(dm);
                }

                if (this is IProductProperties pp && data is Models.IProductProperties dp)
                {
                    pp.LoadFrom(dp);
                }

                if (this is IProductTags pt && data is Models.IProductTags dt)
                {
                    pt.LoadFrom(dt);
                }
            }
        }

        public void LoadFrom(Product data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                SKUCode = data.SKUCode;
                BarCode = data.BarCode;
                Name = data.Name;
                Slug = data.Slug;
                if (
                    string.IsNullOrEmpty(Description)
                    || (!string.IsNullOrEmpty(data.Description) && data.Description.Length > Description.Length)
                )
                {
                    Description = data.Description;
                }
                Category = data.Category;
                if (this is IProductManufacturer pm && data is IProductManufacturer dpm)
                {
                    pm.LoadFrom(dpm);
                }

                if (this is IProductProperties pp && data is IProductProperties dpp)
                {
                    pp.LoadFrom(dpp);
                }

                if (this is IProductTags pt && data is IProductTags dpt)
                {
                    pt.LoadFrom(dpt);
                }
            }
        }

        public override string ToString()
        {
            return string.Format("Name: {0}; BarCode: {1}; SKUCode: {2};", Name, BarCode, SKUCode);
        }
    }
}
