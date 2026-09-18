using System;

namespace Birko.Data.Patterns.IndexManagement
{
    /// <summary>
    /// Exception thrown when an index management operation fails.
    /// </summary>
    public class IndexManagementException : Exception
    {
        /// <summary>
        /// Gets the index name involved in the failed operation.
        /// </summary>
        public string? IndexName { get; }

        /// <summary>
        /// Gets the scope (table/collection/container) involved in the failed operation.
        /// </summary>
        public string? Scope { get; }

        public IndexManagementException(string message)
            : base(message)
        {
        }

        public IndexManagementException(string message, string? indexName, string? scope)
            : base(message)
        {
            IndexName = indexName;
            Scope = scope;
        }

        public IndexManagementException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public IndexManagementException(string message, string? indexName, string? scope, Exception innerException)
            : base(message, innerException)
        {
            IndexName = indexName;
            Scope = scope;
        }
    }
}
