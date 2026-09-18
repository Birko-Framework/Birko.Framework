using System;

namespace Birko.Data.Patterns.Models;

/// <summary>
/// Interface for entities that track who created and last updated them.
/// </summary>
public interface IAuditable
{
    /// <summary>
    /// User ID of the entity creator.
    /// </summary>
    Guid? CreatedBy { get; set; }

    /// <summary>
    /// User ID of the last user to update the entity.
    /// </summary>
    Guid? UpdatedBy { get; set; }
}
