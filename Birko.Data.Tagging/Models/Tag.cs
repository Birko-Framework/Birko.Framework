using Birko.Data.Models;

namespace Birko.Data.Tagging;

/// <summary>
/// A reusable tag that can be attached to any ITaggable entity.
/// Tags are tenant-scoped — each tenant has its own tag namespace.
/// </summary>
public class Tag : AbstractLogModel
{
    public Guid TenantGuid { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public string? TagGroup { get; set; }
}
