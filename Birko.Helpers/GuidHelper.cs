using System;

namespace Birko.Helpers
{
    /// <summary>
    /// Small <see cref="Guid"/> conveniences for API / DTO boundaries.
    /// </summary>
    public static class GuidHelper
    {
        /// <summary>
        /// Treats a null or all-zeros <see cref="Guid"/> as "none": returns <c>null</c> so a caller
        /// can distinguish "server-assigns / no reference" from a real id. A non-empty Guid is
        /// returned unchanged.
        /// </summary>
        public static Guid? Normalize(Guid? guid) => guid is null || guid == Guid.Empty ? null : guid;
    }
}
