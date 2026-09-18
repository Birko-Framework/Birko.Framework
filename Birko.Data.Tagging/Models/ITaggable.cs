namespace Birko.Data.Tagging;

/// <summary>
/// Marker interface for entities that support tagging.
/// Entities implementing this can have tags attached/detached via ITagService.
/// </summary>
public interface ITaggable
{
    /// <summary>
    /// The entity type discriminator used in the EntityTag junction table.
    /// Typically the entity class name (e.g. "Building", "Device").
    /// </summary>
    static abstract string TagEntityType { get; }
}
