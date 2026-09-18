using System;
using Birko.Data.Models;

namespace Birko.Models.Product
{
    public class ProductPartnerCode : AbstractLogModel, ILoadable<ViewModels.ProductPartnerCode>
    {
        public Guid? ProductGuid { get; set; }

        public string PartnerName { get; set; } = null!;

        public string Code { get; set; } = null!;

        public virtual void LoadFrom(ViewModels.ProductPartnerCode data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            ProductGuid = data.ProductGuid;
            PartnerName = data.PartnerName;
            Code = data.Code;
        }
    }
}
