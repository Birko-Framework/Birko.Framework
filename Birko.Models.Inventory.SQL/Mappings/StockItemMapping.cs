using Birko.Models.Inventory;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Inventory.SQL.Mappings
{
    public class StockItemMapping : IModelMapping<StockItem>
    {
        public void Configure(ModelMap<StockItem> map)
        {
            map.ToTable("Items")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Code).HasPrecision(256);
            map.Property(x => x.BarCode).HasPrecision(256);
            map.Property(x => x.Name).HasPrecision(1024);
            map.Property(x => x.ShortName).HasPrecision(256);
            map.Property(x => x.Type).HasPrecision(256);
        }
    }
}
