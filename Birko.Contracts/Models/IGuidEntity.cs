using System;

namespace Birko.Data.Models
{
    /// <summary>
    /// Entity with a nullable Guid identifier.
    /// Implemented by both AbstractModel and ModelViewModel to enable
    /// bidirectional mapping without circular type references.
    /// </summary>
    public interface IGuidEntity
    {
        Guid? Guid { get; set; }
    }
}
