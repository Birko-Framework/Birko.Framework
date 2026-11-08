using System;
using System.Collections.Generic;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// The column names one provider rejects when Birko emits them (TASK-862): its reserved words, as measured live,
    /// not its full keyword list.
    /// </summary>
    /// <remarks>
    /// Column identifiers are emitted bare (rule 17), so a reserved word as a column name is a syntax error, or on
    /// some providers a value function such as <c>USER</c> or <c>CURRENT_DATE</c> read in place of the column. Each
    /// provider's connector holds exactly one instance (rule 20), and
    /// <see cref="AbstractConnectorBase.RequireUsableColumnNames"/> consults it before any SQL is sent.
    /// </remarks>
    public sealed class RejectedColumnNames
    {
        private readonly HashSet<string> _names;

        /// <param name="provider">The provider's name, as the refusal message shows it.</param>
        /// <param name="measuredOn">The server version the list was measured against, and the catalogue it came from.</param>
        /// <param name="names">The rejected names; compared without regard to case.</param>
        public RejectedColumnNames(string provider, string measuredOn, IEnumerable<string> names)
        {
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            MeasuredOn = measuredOn ?? throw new ArgumentNullException(nameof(measuredOn));
            _names = new HashSet<string>(names ?? throw new ArgumentNullException(nameof(names)), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The provider's name, as the refusal message shows it.</summary>
        public string Provider { get; }

        /// <summary>The server version the list was measured against, and the catalogue it came from.</summary>
        public string MeasuredOn { get; }

        /// <summary>Every rejected name.</summary>
        public IReadOnlyCollection<string> Names => _names;

        /// <summary>Whether <paramref name="columnName"/> is rejected, ignoring case.</summary>
        public bool Contains(string? columnName) => columnName != null && _names.Contains(columnName);
    }
}
