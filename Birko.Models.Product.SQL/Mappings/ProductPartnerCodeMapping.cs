using Birko.Models.Product;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Product.SQL.Mappings
{
    public class ProductPartnerCodeMapping : IModelMapping<ProductPartnerCode>
    {
        public void Configure(ModelMap<ProductPartnerCode> map)
        {
            map.ToTable("ProductPartnerCodes")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.PartnerName).HasMaxLength(256);
            map.Property(x => x.Code).HasMaxLength(256);
        }
    }
}
