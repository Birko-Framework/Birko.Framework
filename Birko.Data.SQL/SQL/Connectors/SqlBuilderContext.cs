using System.Data;
using System.Data.Common;
using System.Text;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// Context class for SQL building operations
    /// </summary>
    public class SqlBuilderContext
    {
        private static readonly System.Text.RegularExpressions.Regex _sanitizeRegex =
            new(@"[^a-zA-Z0-9_]", System.Text.RegularExpressions.RegexOptions.Compiled);

        private readonly StringBuilder _stringBuilder;
        private readonly AbstractConnectorBase _connector;

        // TASK-836: the last name generated, the command it was generated for, and that command's parameter count
        // at the time. A name added while all three still hold cannot be on the command yet.
        private string? _generatedName;
        private DbCommand? _generatedFor;
        private int _generatedAtCount;

        public SqlBuilderContext(AbstractConnectorBase connector)
        {
            _connector = connector ?? throw new ArgumentNullException(nameof(connector));
            _stringBuilder = new StringBuilder();
        }

        /// <summary>TASK-869 — the provider's dialect, which renders a string comparison with an explicit StringComparison.</summary>
        internal Conditions.SqlValueFragmentDialect Dialect => _connector.RenderDialect;

        /// <summary>The longest field-name part of a generated parameter name.</summary>
        private const int MaxNameStem = 64;

        /// <summary>
        /// Generates a unique parameter name for the given field
        /// </summary>
        public string GenerateParameterName(string fieldName, int index, DbCommand command)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                throw new ArgumentException("Field name cannot be null or empty", nameof(fieldName));
            }

            var count = command.Parameters?.Count ?? 0;
            var sanitizedName = _sanitizeRegex.Replace(fieldName, string.Empty);
            // TASK-857: the field name may be a value fragment (a rounded CASE runs to hundreds of characters), and SQL Server
            // refuses a parameter name over 128. The name only has to be unique, which the count suffix already makes it.
            if (sanitizedName.Length > MaxNameStem) sanitizedName = sanitizedName.Substring(0, MaxNameStem);
            var name = $"@WHERE{sanitizedName}{index}_{count}";
            _generatedName = name;
            _generatedFor = command;
            _generatedAtCount = count;
            return name;
        }

        /// <summary>
        /// Formats a value based on the condition type (e.g., adds wildcards for LIKE operations)
        /// </summary>
        public object? FormatValue(object? value, Conditions.ConditionType type)
        {
            if (value is string str)
            {
                return type switch
                {
                    Conditions.ConditionType.StartsWith => $"{EscapeLikePattern(str)}%",
                    Conditions.ConditionType.Like => $"%{EscapeLikePattern(str)}%",
                    Conditions.ConditionType.EndsWith => $"%{EscapeLikePattern(str)}",
                    _ => str,
                };
            }
            return value;
        }

        /// <summary>
        /// The escape character every LIKE this framework renders declares (<c>LIKE @p ESCAPE '!'</c>). Not the
        /// backslash: inside a MySQL string literal a backslash is itself an escape, so <c>ESCAPE ''</c> is a
        /// syntax error there.
        /// </summary>
        public const char LikeEscapeCharacter = '!';

        /// <summary>
        /// SH-M134 (TASK-153). Makes a search term literal inside a LIKE pattern. C#'s <c>Contains</c> /
        /// <c>StartsWith</c> / <c>EndsWith</c> treat every character literally, but SQL reads <c>%</c> and <c>_</c> as
        /// wildcards (and SQL Server reads <c>[</c> as a character class), so <c>StartsWith("A_1")</c> matched
        /// <c>A21</c>. Each is prefixed with <see cref="LikeEscapeCharacter"/>, as is that character itself.
        /// </summary>
        public static string EscapeLikePattern(string value)
        {
            if (value.IndexOfAny(LikeMetacharacters) < 0)
                return value;
            var sb = new System.Text.StringBuilder(value.Length + 4);
            foreach (var c in value)
            {
                if (Array.IndexOf(LikeMetacharacters, c) >= 0)
                    sb.Append(LikeEscapeCharacter);
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static readonly char[] LikeMetacharacters = { LikeEscapeCharacter, '%', '_', '[' };

        /// <summary>
        /// Escapes a value to prevent SQL injection (fallback when parameters can't be used)
        /// </summary>
        public object? EscapeValue(object? item)
        {
            if (item is string str)
            {
                // TASK-253: one producer for the quote-doubling rule. This sink legitimately escapes
                // rather than parameterises — see SqlLiteral for the two cases where that is true.
                return SqlLiteral.EscapeLiteral(str);
            }
            return item;
        }

        /// <summary>
        /// Adds a parameter to the command
        /// </summary>
        /// <remarks>
        /// TASK-836: the name generated last, added to the same command before anything else was, skips the
        /// connector's lookup for an existing parameter of that name. It cannot exist: every generated name carries the
        /// command's parameter count when it was generated, and that count only grows. Any other name takes the
        /// lookup and overwrites a parameter of that name.
        /// </remarks>
        public void AddParameter(DbCommand command, string name, object? value)
        {
            if (IsFreshlyGenerated(command, name))
            {
                _generatedName = null;
                _generatedFor = null;
                _connector.AddUniqueParameter(command, name, value);
                return;
            }
            _connector.AddParameter(command, name, value);
        }

        private bool IsFreshlyGenerated(DbCommand command, string name)
            => ReferenceEquals(command, _generatedFor)
               && string.Equals(name, _generatedName, StringComparison.Ordinal)
               && command.Parameters.Count == _generatedAtCount;
    }
}
