using System;
using Birko.Data.SQL.Attributes;
using Birko.Data.Models;

namespace Birko.Models.Product
{
    [Table("ProductPartnerCodes")]
    public class ProductPartnerCode : AbstractDatabaseLogModel, ILoadable<ViewModels.ProductPartnerCode>
    {
        public Guid? ProductGuid { get; set; }

        [PrecisionField(256)]
        public string PartnerName { get; set; } = null!;

        [PrecisionField(256)]
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
