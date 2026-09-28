using Birko.Data.Models;

namespace Birko.Data.Tagging;

/// <summary>
/// Junction record linking a Tag to an entity.
/// Uses EntityType as a string discriminator so one table serves all entity types.
/// </summary>
public class EntityTag : AbstractLogModel
{
    public Guid TenantGuid { get; set; }
    public Guid TagGuid { get; set; }
    public Guid EntityGuid { get; set; }
    public string EntityType { get; set; } = string.Empty;
}
