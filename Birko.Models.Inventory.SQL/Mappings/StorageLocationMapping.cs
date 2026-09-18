using Birko.Models.Inventory;
using Birko.Models.SQL.Mapping;

namespace Birko.Models.Inventory.SQL.Mappings
{
    public class StorageLocationMapping : IModelMapping<StorageLocation>
    {
        public void Configure(ModelMap<StorageLocation> map)
        {
            map.ToTable("Repositories")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);

            map.Property(x => x.Title).HasPrecision(256);
            // CR-L311: dropped a redundant HasColumnName("SortOrder") — the column name already defaults to
            // the property name, and SortOrder (an int) needs no facet, so it maps by default.
        }
    }
}
