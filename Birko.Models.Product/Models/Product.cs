using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Models.Product
{
    public class Product : Data.Models.AbstractLogModel, Data.Models.ILoadable<Birko.Models.Product.ViewModels.Product>
    {
        public string SKUCode { get; set; } = null!;

        public string BarCode { get; set; } = null!;

        public string Name { get; set; } = null!;

        public string Slug { get; set; } = null!;

        public string Description { get; set; } = null!;

        public string Category { get; set; } = null!;

        public void LoadFrom(Birko.Models.Product.ViewModels.Product data)
        {
            base.LoadFrom(data);
            if (data == null) return;

            SKUCode = data.SKUCode;
            BarCode = data.BarCode;
            Slug = data.Slug;
            Name = data.Name;
            Description = data.Description;
            Category = data.Category;

            if (this is IProductManufacturer pm && data is ViewModels.IProductManufacturer dpm)
            {
                pm.LoadManufacturers(dpm.Manufacturer);
            }

            if (this is IProductProperties pp && data is ViewModels.IProductProperties dpp)
            {
                pp.LoadProperties(dpp.Properties);
            }

            if (this is IProductTags pt && data is ViewModels.IProductTags dpt)
            {
                pt.LoadTags(dpt.Tags);
            }
        }
    }
}
