using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Models
{
    public class Product : Data.Models.AbstractLogModel, Data.Models.ILoadable<ViewModels.Product>
    {
        public string SKUCode { get; set; }

        public string BarCode { get; set; }

        public string Name { get; set; }

        public string Slug { get; set; }

        public string Description { get; set; }

        public string Category { get; set; }

        public void LoadFrom(ViewModels.Product data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                SKUCode = data.SKUCode;
                BarCode = data.BarCode;
                Slug = data.Slug;
                Name = data.Name;
                Description = data.Description;
                Category = data.Category;

                if (this is IProductManufacturer pm && data is ViewModels.IProductManufacturer dpm)
                {
                    pm.LoadFrom(dpm);
                }

                if (this is IProductProperties pp && data is ViewModels.IProductProperties dpp)
                {
                    pp.LoadFrom(dpp);
                }

                if (this is IProductTags pt && data is ViewModels.IProductTags dpt)
                {
                    pt.LoadFrom(dpt);
                }
            }
        }
    }
}
