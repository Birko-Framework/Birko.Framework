using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using System.Collections;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Collections.Concurrent;
using System.Data;
using Birko.Configuration;

namespace Birko.Data.SQL
{
    public static partial class DataBase
    {
        private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, AbstractConnector>> _connectors = new();
        private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, AbstractAsyncConnector>> _asyncConnectors = new();
        private static readonly ConcurrentDictionary<Type, Func<object>> _instanceFactories = new();

        /// <summary>
        /// Returns a compiled parameterless constructor delegate for <paramref name="type"/>,
        /// cached per type. Avoids Activator.CreateInstance reflection on every row.
        /// </summary>
        internal static Func<object> GetOrCreateInstanceFactory(Type type)
        {
            return _instanceFactories.GetOrAdd(type, static t =>
            {
                var ctor = t.GetConstructor(Type.EmptyTypes)
                    ?? throw new InvalidOperationException($"Type {t.FullName} has no parameterless constructor");
                var newExpr = Expression.New(ctor);
                var lambda = Expression.Lambda<Func<object>>(Expression.Convert(newExpr, typeof(object)));
                return lambda.Compile();
            });
        }
        // Keyed by Expression reference identity — avoids ToString() traversal and prevents memory leaks via weak refs
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Expression, Func<object>> _expressionCache = new();
        // Memoizes ContainsParameter results by Expression reference to avoid repeated subtree walks
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Expression, System.Runtime.CompilerServices.StrongBox<bool>> _containsParamCache = new();

        /// <summary>
        /// Extension point for resolving field select names from non-table sources (e.g. views).
        /// Parameters: (Type exprType, string propertyName, bool withTableName) → field select name or null.
        /// Set by Birko.Data.SQL.View to break the dependency from SQL → View.
        /// </summary>
        public static Func<Type, string, bool, string?>? ResolveFieldSelectName { get; set; }

        private static readonly System.Threading.AsyncLocal<Func<Type, string, bool, string?>?> _scopedFieldResolver = new();

        /// <summary>
        /// Resolves field names through <paramref name="resolver"/> for the current async flow until the returned scope is
        /// disposed, ahead of <see cref="ResolveFieldSelectName"/>. Per caller, never process-wide (rule 36): a store that
        /// knows its own view resolves a filter on it without registering anything global (TASK-151, SH-M405).
        /// </summary>
        public static IDisposable ScopeFieldResolver(Func<Type, string, bool, string?> resolver)
        {
            var previous = _scopedFieldResolver.Value;
            _scopedFieldResolver.Value = resolver ?? throw new ArgumentNullException(nameof(resolver));
            return new ResolverScope(previous);
        }

        private sealed class ResolverScope : IDisposable
        {
            private readonly Func<Type, string, bool, string?>? _previous;
            private bool _disposed;
            public ResolverScope(Func<Type, string, bool, string?>? previous) => _previous = previous;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _scopedFieldResolver.Value = _previous;
            }
        }
        public static AbstractConnector GetConnector<T>(Settings settings) where T : AbstractConnector
        {
            var connectorType = typeof(T);
            var settingsId = settings.GetId();

            var connectorDict = _connectors.GetOrAdd(connectorType, _ => new ConcurrentDictionary<string, AbstractConnector>());
            return connectorDict.GetOrAdd(settingsId, id =>
            {
                return (AbstractConnector)Activator.CreateInstance(typeof(T), new object[] { settings })!;
            });
        }

        public static AbstractAsyncConnector GetAsyncConnector<T>(Settings settings) where T : AbstractAsyncConnector
        {
            var connectorType = typeof(T);
            var settingsId = settings.GetId();

            var connectorDict = _asyncConnectors.GetOrAdd(connectorType, _ => new ConcurrentDictionary<string, AbstractAsyncConnector>());
            return connectorDict.GetOrAdd(settingsId, id =>
            {
                return (AbstractAsyncConnector)Activator.CreateInstance(typeof(T), new object[] { settings })!;
            });
        }

        private static void GetProperties(Type type, Action<PropertyInfo> action)
        {
            foreach (var field in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                var accesors = field.GetAccessors();
                // SH-M240 (TASK-154): a get-only property (`Display => First + Last`) has no setter, so it was a column
                // that every read failed to set. A private setter still counts (CanWrite), as reflection can use it.
                if (accesors.Any(x => !x.IsStatic) && field.CanWrite)
                {
                    action?.Invoke(field);
                }
            }
        }

        /// <summary>
        /// The command text with each parameter's value written in place of its name. Diagnostic only (the
        /// <c>InitException</c> command text); never executed.
        /// </summary>
        /// <remarks>
        /// One pass over the text (TASK-832). At a position where a parameter name can start, the <b>longest</b>
        /// name matching there is substituted: names are suffixed with <c>_{count}</c>
        /// (<c>SqlBuilderContext.GenerateParameterName</c>), so <c>@WHEREName0_5</c> is a prefix of
        /// <c>@WHEREName0_50</c>, and <c>@LIMIT</c> of <c>@LIMITxxx</c>. The text is written once, so a value
        /// that happens to contain a parameter name is not rewritten either.
        /// <para>Measured before: one <c>StringBuilder.Replace</c> per parameter, each scanning the whole text of
        /// a builder it kept fragmenting. Every SQL read builds this string eagerly, and for
        /// <c>ids.Contains(x.Guid)</c> with 2,000 ids it took 6.5 s of a 6.8 s read on SQLite (Symbio's
        /// FIELD-051).</para>
        /// </remarks>
        public static string GetGeneratedQuery(DbCommand dbCommand)
        {
            var text = dbCommand.CommandText ?? string.Empty;
            if (dbCommand.Parameters.Count == 0 || text.Length == 0)
            {
                return text;
            }

            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            var starts = new HashSet<char>();
            int longest = 0;
            foreach (DbParameter parameter in dbCommand.Parameters)
            {
                var name = parameter.ParameterName;
                if (string.IsNullOrEmpty(name) || values.ContainsKey(name))
                {
                    continue;
                }
                values[name] = RenderDiagnosticValue(parameter.Value);
                starts.Add(name[0]);
                longest = Math.Max(longest, name.Length);
            }

            var lookup = values.GetAlternateLookup<ReadOnlySpan<char>>();
            var query = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (starts.Contains(text[i]))
                {
                    var matched = 0;
                    string? value = null;
                    for (int length = Math.Min(longest, text.Length - i); length > 0; length--)
                    {
                        if (lookup.TryGetValue(text.AsSpan(i, length), out value))
                        {
                            matched = length;
                            break;
                        }
                    }
                    if (matched > 0)
                    {
                        query.Append(value);
                        i += matched;
                        continue;
                    }
                }
                query.Append(text[i]);
                i++;
            }

            return query.ToString();
        }

        /// <summary>Bytes of a <c>byte[]</c> written out in the diagnostic text before it is truncated.</summary>
        private const int DiagnosticBytesShown = 32;

        /// <summary>
        /// A parameter value as it reads in <see cref="GetGeneratedQuery"/>. By the value's CLR type, never by
        /// <c>DbType</c> (TASK-853): <c>SqliteParameter</c> defaults to <c>DbType.String</c>, so a type-driven
        /// render quoted every number on SQLite, and a null with any non-string type rendered nothing. Numbers and
        /// dates use the invariant culture, so the text does not depend on the host (sk-SK wrote <c>1,5</c>).
        /// </summary>
        private static string RenderDiagnosticValue(object? value)
        {
            static string Quoted(string text) => "'" + SqlLiteral.EscapeLiteral(text) + "'";
            var invariant = System.Globalization.CultureInfo.InvariantCulture;

            switch (value)
            {
                case null:
                case DBNull:
                    return "NULL";
                // TASK-850: a quote inside the value is doubled by the framework's one escaping producer (rule 20).
                case string text:
                    return Quoted(text);
                case char character:
                    return Quoted(character.ToString());
                case bool flag:
                    return flag ? "1" : "0";
                case Enum enumValue:
                    return Convert.ChangeType(enumValue, Enum.GetUnderlyingType(enumValue.GetType()), invariant) is IFormattable number
                        ? number.ToString(null, invariant)
                        : Quoted(enumValue.ToString());
                case Guid guid:
                    return Quoted(guid.ToString("D"));
                case DateTime dateTime:
                    return Quoted(dateTime.ToString("O", invariant));
                case DateTimeOffset dateTimeOffset:
                    return Quoted(dateTimeOffset.ToString("O", invariant));
                case DateOnly date:
                    return Quoted(date.ToString("yyyy-MM-dd", invariant));
                case TimeOnly time:
                    return Quoted(time.ToString("HH:mm:ss.FFFFFFF", invariant).TrimEnd('.'));
                case TimeSpan span:
                    return Quoted(span.ToString("c", invariant));
                case byte[] bytes:
                    return RenderDiagnosticBytes(bytes);
                case sbyte or byte or short or ushort or int or uint or long or ulong or decimal:
                    return ((IFormattable)value).ToString(null, invariant);
                case double or float:
                    // Shortest round-trip form; NaN and infinities are not SQL numbers, so they are quoted.
                    var real = ((IFormattable)value).ToString("R", invariant);
                    return double.IsFinite(Convert.ToDouble(value, invariant)) ? real : Quoted(real);
                case IFormattable formattable:
                    return Quoted(formattable.ToString(null, invariant));
                default:
                    return Quoted(value.ToString() ?? string.Empty);
            }
        }

        private static string RenderDiagnosticBytes(byte[] bytes)
        {
            if (bytes.Length <= DiagnosticBytesShown)
            {
                return "0x" + Convert.ToHexString(bytes);
            }
            return "0x" + Convert.ToHexString(bytes, 0, DiagnosticBytesShown) + "... (" + bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes)";
        }

        public static Conditions.Condition CreateCondition(AbstractField field, object value)
        {
            return new Conditions.Condition(field.Name, new[] { field.Property.GetValue(value, null) });
        }

        public static string? ParseExpression(Expression expr, IDictionary<string, object> parameters, bool withTableName = false, Type? exprType = null)
            => ParseExpression(expr, parameters, withTableName, exprType, null);

        /// <summary>
        /// Renders an update's SET value (<paramref name="expr"/>) to SQL, binding its constants into <paramref name="parameters"/>.
        /// </summary>
        /// <param name="dialect">
        /// TASK-860 — the connector's fragment dialect, for the steps a provider spells its own way: an integer division
        /// (MySQL's <c>/</c> divides fractions), an exact widening cast, a unary minus (SQLite's decimals). Null is the
        /// portable form.
        /// </param>
        /// <remarks>
        /// TASK-860: anything the method cannot translate exactly is refused by name with <see cref="NotSupportedException"/>
        /// before the statement runs. Before (measured), a unary minus, a <c>~</c> or a <c>!</c> rendered nothing
        /// (<c>SET Hits = WHERE …</c>, a syntax error), a bitwise <c>&amp;</c> / <c>|</c> rendered <c>AND</c> / <c>OR</c> (SQLite
        /// stored 1 for <c>Hits | 1</c>), a <c>^</c> or <c>&lt;&lt;</c> rendered no operator, a narrowing cast was dropped (MySQL
        /// stored 11 for <c>(int)10.5m</c>, which C# truncates to 10) and MySQL divided integers as fractions.
        /// </remarks>
        public static string? ParseExpression(Expression expr, IDictionary<string, object> parameters, bool withTableName, Type? exprType, SqlValueFragmentDialect? dialect)
        {
            if (expr == null) return null;
            dialect ??= SqlValueFragmentDialect.Portable;
            if (expr is LambdaExpression lambdaExpression)
            {
                var type = lambdaExpression.Parameters?.FirstOrDefault()?.Type;
                // TASK-860: GetValueOrDefault becomes ??, a typed Equals ==, before the normalizer runs.
                var rewritten = RewriteTranslatableCalls(lambdaExpression.Body);
                var normalizedBody = Birko.Data.Expressions.ExpressionNormalizer.Normalize(rewritten) ?? rewritten;
                // An integer narrowed to the column's own type (`SET Sh = (short)(Sh + 1)`): stored as SQL computed it, which
                // is C#'s value until it overflows, and an overflow is the server's error on a provider that is loud.
                if (normalizedBody is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } narrowed
                    && narrowed.Type == lambdaExpression.ReturnType && ContainsParameter(narrowed)
                    && SqlConversion.IsIntegralNarrowing(narrowed.Operand.Type, narrowed.Type))
                {
                    if (!dialect.StoresIntegerOverflowLoudly)
                        throw new NotSupportedException(
                            $"Cannot translate the conversion from {Describe(narrowed.Operand.Type)} to {Describe(narrowed.Type)} in an "
                            + "update's SET value on this provider: C# wraps a value that overflows, and this provider stores it "
                            + "unchanged, out of the column's range, with no error. Compute the value in memory and set it.");
                    return ParseExpression(narrowed.Operand, parameters, withTableName, type, dialect);
                }
                return ParseExpression(normalizedBody, parameters, withTableName, type, dialect);
            }
            if (expr is ConditionalExpression conditionalExpression)
            {
                // Value-position ternary (e.g. an Update SET right-hand side): render as CASE WHEN.
                // SH-M136 (TASK-153): a bare bool column is not a predicate on SQL Server (BIT, error 4145), so
                // it is rendered as the same test the WHERE-side renderer uses.
                // The column is resolved by this method, so it is qualified only when the caller asked
                // (an UPDATE's SET is not: PostgreSQL rejects `"T".col` there against `UPDATE "T"`).
                var testExpr = UnwrapConvert(conditionalExpression.Test);
                var test = testExpr is MemberExpression boolMember
                        && UnwrapNullable(boolMember.Type) == typeof(bool)
                        && TryResolveParameterColumn(boolMember, exprType, out _)
                    ? BoolColumnTest(ParseExpression(boolMember, parameters, withTableName, exprType, dialect)!)
                    : ParseExpression(conditionalExpression.Test, parameters, withTableName, exprType, dialect);
                var ifTrue = ParseExpression(conditionalExpression.IfTrue, parameters, withTableName, exprType, dialect);
                var ifFalse = ParseExpression(conditionalExpression.IfFalse, parameters, withTableName, exprType, dialect);
                return $"CASE WHEN {test} THEN {ifTrue} ELSE {ifFalse} END";
            }
            if (expr is BinaryExpression binaryExpression)
            {
                if (binaryExpression.NodeType == ExpressionType.Coalesce)
                {
                    // Value-position null-coalescing (a ?? b) → COALESCE(a, b).
                    var coalesceLeft = ParseExpression(binaryExpression.Left, parameters, withTableName, exprType, dialect);
                    var coalesceRight = ParseExpression(binaryExpression.Right, parameters, withTableName, exprType, dialect);
                    return $"COALESCE({coalesceLeft}, {coalesceRight})";
                }
                if (binaryExpression.NodeType is ExpressionType.Equal or ExpressionType.NotEqual
                    && TryGetNullComparisonOperand(binaryExpression, out var nonNullOperand))
                {
                    // `x IS NULL` / `x IS NOT NULL` — a null constant compared with `=`/`<>` is always
                    // UNKNOWN in SQL, so it must become IS [NOT] NULL (matters for CASE WHEN tests above).
                    var operand = ParseExpression(nonNullOperand!, parameters, withTableName, exprType, dialect);
                    return binaryExpression.NodeType == ExpressionType.Equal
                        ? $"({operand} IS NULL)"
                        : $"({operand} IS NOT NULL)";
                }
                var sqlOperator = SetValueOperator(binaryExpression);
                if (sqlOperator == null)
                    throw RefuseNode(binaryExpression);
                if (binaryExpression.NodeType is ExpressionType.Divide or ExpressionType.Modulo
                    && UnwrapNullable(binaryExpression.Type) == typeof(decimal)
                    && (IsWideningOverRow(binaryExpression.Left) || IsWideningOverRow(binaryExpression.Right)))
                {
                    // `(decimal)x.Hits / x.Other`: the integer is cast to DECIMAL(10,0), and each provider picks the scale of
                    // that quotient itself (SQL Server keeps as few as six decimals); SQLite's native `/` divides it as a
                    // double. A double quotient (`x.Hits / 2.0`) is IEEE division everywhere and is translated.
                    throw new NotSupportedException(
                        $"Cannot translate the {binaryExpression.NodeType} of an integer converted to decimal in an update's SET "
                        + "value to SQL: the scale of the quotient is each provider's own (SQL Server keeps as few as six "
                        + "decimals), and SQLite divides it as a double. Compute the value in memory and set it.");
                }
                var left = ParseExpression(binaryExpression.Left, parameters, withTableName, exprType, dialect);
                var right = ParseExpression(binaryExpression.Right, parameters, withTableName, exprType, dialect);
                if (binaryExpression.NodeType == ExpressionType.Divide && SqlConversion.IsIntegral(binaryExpression.Type))
                    return dialect.IntegralDivide(left!, right!);
                return $"({left} {sqlOperator} {right})";
            }
            if (expr is MethodCallExpression callExpression)
            {
                if (callExpression.Method.Name == "Replace" && callExpression.Method.DeclaringType == typeof(string)
                    && callExpression.Arguments.Count == 2 && callExpression.Object != null)
                {
                    StringBuilder result = new StringBuilder();
                    //maybe platform specific implementation
                    result.AppendFormat("REPLACE({0}", ParseExpression(callExpression.Object, parameters, withTableName, exprType, dialect));
                    foreach (var argument in callExpression.Arguments)
                    {
                        result.AppendFormat(", {0}", ParseExpression(argument, parameters, withTableName, exprType, dialect));
                    }
                    result.Append(")");
                    return result.ToString();
                }
                if (IsCaseFold(callExpression))
                {
                    var inner = ParseExpression(callExpression.Object!, parameters, withTableName, exprType, dialect);
                    return callExpression.Method.Name is "ToLower" or "ToLowerInvariant" ? $"LOWER({inner})" : $"UPPER({inner})";
                }
                // SH-H024 / TASK-308: only a parameter-FREE call can be folded to a constant. The
                // normalizer has already folded every such call, so a parameter-bound one arriving
                // here cannot be expressed as a SET value at all — and it must not be *invoked*.
                // Measured on SQLite before this guard: `SET Name = string.Concat(r.Name, "-",
                // r.Name)` reflectively invoked Concat with the parameter arguments evaluated to
                // null and stored the result, "-", with no exception and no log entry.
                if (ContainsParameter(callExpression))
                {
                    throw new NotSupportedException(
                        $"The value expression cannot be translated to SQL: "
                        + $"{callExpression.Method.DeclaringType?.Name}.{callExpression.Method.Name} "
                        + "is not one of the translated calls (Replace, ToLower, ToUpper without a culture, GetValueOrDefault) "
                        + "and it references the entity, so it cannot be evaluated to a constant either. "
                        + "Before this was refused it was invoked reflectively with the entity's "
                        + "arguments evaluated to null and the result written to the column. "
                        + "Compute the value in memory and pass it, or use a translated call.");
                }

                var key = "@Const" + parameters.Count;
                var value = EvaluateExpression(callExpression);
                parameters.Add(key, value!);
                return key;
            }
            if (expr is UnaryExpression unaryExpression && ContainsParameter(unaryExpression))
            {
                switch (unaryExpression.NodeType)
                {
                    case ExpressionType.Convert:
                    case ExpressionType.ConvertChecked:
                        // TASK-860: a lossy conversion is refused, a widening to a fraction cast explicitly.
                        RequireTranslatableConversion(unaryExpression.Operand.Type, unaryExpression.Type);
                        return dialect.Conversion(
                            ParseExpression(unaryExpression.Operand, parameters, withTableName, exprType, dialect)!,
                            unaryExpression.Operand.Type, unaryExpression.Type);
                    case ExpressionType.Negate:
                    case ExpressionType.NegateChecked:
                        return dialect.Negate(
                            ParseExpression(unaryExpression.Operand, parameters, withTableName, exprType, dialect)!, unaryExpression.Type);
                    case ExpressionType.UnaryPlus:
                        return ParseExpression(unaryExpression.Operand, parameters, withTableName, exprType, dialect);
                    default:
                        throw RefuseNode(unaryExpression);
                }
            }
            if (expr is MemberExpression memberExpression)
            {
                // Handle DateTime property accessors like x.OpenedAt.Date → DATE(column)
                if (memberExpression.Member.Name == "Date"
                    && memberExpression.Member.DeclaringType == typeof(DateTime)
                    && memberExpression.Expression is MemberExpression innerDateMember)
                {
                    var columnExpr = ParseExpression(innerDateMember, parameters, withTableName, exprType, dialect);
                    if (!string.IsNullOrEmpty(columnExpr) && !columnExpr.StartsWith("@"))
                    {
                        return $"DATE({columnExpr})";
                    }
                    // If inner resolved to a parameter, evaluate the whole .Date as constant
                    var key = "@Const" + parameters.Count;
                    var value = EvaluateExpression(memberExpression);
                    parameters.Add(key, value!);
                    return key;
                }

                string name = string.Empty;
                var isParamAccess = memberExpression.Expression?.NodeType == ExpressionType.Parameter
                    || memberExpression.Expression?.NodeType == ExpressionType.TypeAs
                    || (memberExpression.Expression is UnaryExpression convExpr
                        && convExpr.NodeType == ExpressionType.Convert
                        && convExpr.Operand.NodeType == ExpressionType.Parameter);
                if (
                    exprType != null
                    && memberExpression.Member.ReflectedType?.IsAssignableFrom(exprType) == true
                    && isParamAccess
                )
                {
                    name = ResolveColumnName(exprType, memberExpression.Member.Name, withTableName) ?? string.Empty;
                }
                if (!string.IsNullOrEmpty(name))
                    return name;
                if (memberExpression.Expression is ConstantExpression
                    || memberExpression.Expression == null)
                {
                    var key = "@Const" + parameters.Count;
                    var value = EvaluateExpression(memberExpression);
                    parameters.Add(key, value!);
                    return key;
                }
                if (!ContainsParameter(memberExpression))
                {
                    // A closure chain (`filter.Range.From`): a value, bound like any other constant.
                    var key = "@Const" + parameters.Count;
                    parameters.Add(key, EvaluateExpression(memberExpression)!);
                    return key;
                }
                // SH-M149 (TASK-153): `x.Sub.Prop` is no column. Recursing (without the type) produced a
                // null fragment and SQL such as `( = @Const0)`, a syntax error far from its cause.
                throw new NotSupportedException(
                    $"Cannot translate '{memberExpression}' to SQL: it is not a mapped column of the entity.");
            }
            if (expr is ConstantExpression constantExpression)
            {
                var key = "@Const" + parameters.Count;
                parameters.Add(key, constantExpression.Value!);
                return key;
            }
            // TASK-860: no other node kind has a SET translation; a parameter-free one is a constant.
            if (ContainsParameter(expr))
                throw RefuseNode(expr);
            var constantKey = "@Const" + parameters.Count;
            parameters.Add(constantKey, EvaluateExpression(expr)!);
            return constantKey;
        }

        /// <summary>
        /// TASK-860 — the SQL operator of a binary node in an update's SET value, or null when it has none there. A
        /// bitwise <c>&amp;</c> / <c>|</c> on integers is not <c>AND</c> / <c>OR</c>, and <c>^</c>, the shifts and array indexing
        /// have no portable form, so they answer null and are refused.
        /// </summary>
        private static string? SetValueOperator(BinaryExpression binary)
        {
            var logical = UnwrapNullable(binary.Type) == typeof(bool);
            var numeric = IsNumericType(binary.Type);
            return binary.NodeType switch
            {
                ExpressionType.Add or ExpressionType.AddChecked when numeric => "+",
                ExpressionType.Subtract or ExpressionType.SubtractChecked when numeric => "-",
                ExpressionType.Multiply or ExpressionType.MultiplyChecked when numeric => "*",
                ExpressionType.Divide when numeric => "/",
                ExpressionType.Modulo when numeric => "%",
                ExpressionType.GreaterThan => ">",
                ExpressionType.GreaterThanOrEqual => ">=",
                ExpressionType.LessThan => "<",
                ExpressionType.LessThanOrEqual => "<=",
                ExpressionType.Equal => "=",
                ExpressionType.NotEqual => "<>",
                ExpressionType.And or ExpressionType.AndAlso when logical => "AND",
                ExpressionType.Or or ExpressionType.OrElse when logical => "OR",
                _ => null,
            };
        }

        /// <summary>An integer over the row converted to a fraction (<see cref="SqlConversionKind.Widening"/>).</summary>
        private static bool IsWideningOverRow(Expression operand)
            => operand is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion
                && ContainsParameter(conversion)
                && SqlConversion.Classify(conversion.Operand.Type, conversion.Type) == SqlConversionKind.Widening;

        /// <summary>
        /// TASK-596. The values a column is compared with are bound in the column's <b>stored</b> form, or the comparison
        /// runs against a shape the column never holds. Measured on SQLite: <c>r.At == instant</c> on a <c>[UtcField]</c>
        /// column bound <c>'2026-10-14 09:30:15.123'</c> against the stored <c>'2026-10-14 09:30:15.123+00:00'</c>, and
        /// matched no row an entity had written. The column side of <paramref name="comparison"/> resolves to its
        /// field, and every value goes through <see cref="Fields.AbstractField.ToStoredValue"/>, the one producer an
        /// entity write and a <c>PropertyUpdate</c> Set also use.
        /// <para>TASK-606: an <c>IN</c> list (<c>values.Contains(x.Col)</c>) goes through the same helper. Its values were
        /// bound raw too, so on a <c>[UtcField]</c> column <c>IN</c> matched nothing and <c>NOT IN</c> excluded nothing.</para>
        /// </summary>
        private static void StoreComparedValues(Conditions.Condition condition, BinaryExpression comparison, Type? exprType)
            => StoreColumnValues(condition, exprType, comparison.Left, comparison.Right);

        /// <summary>
        /// Rewrites <paramref name="condition"/>'s values into the stored form of the first plain column among
        /// <paramref name="candidates"/>. A condition with sub-conditions, no values, or no plain column is left alone.
        /// </summary>
        private static void StoreColumnValues(Conditions.Condition condition, Type? exprType, params Expression?[] candidates)
        {
            if (exprType == null || condition.Values == null || condition.SubConditions?.Any() == true)
                return;
            var field = candidates.Where(c => c != null).Select(c => ComparedField(c!, exprType)).FirstOrDefault(f => f != null);
            if (field == null)
                return;
            condition.Values = condition.Values.Cast<object?>().Select(v => v == null ? null : field.ToStoredValue(v)).ToArray();
        }

        private static Fields.AbstractField? ComparedField(Expression side, Type exprType)
        {
            while (side is UnaryExpression { NodeType: ExpressionType.Convert } convert)
                side = convert.Operand;
            if (side is not MemberExpression member || !ContainsParameter(member))
                return null;
            var isParameterAccess = member.Expression?.NodeType == ExpressionType.Parameter
                || (member.Expression is UnaryExpression { NodeType: ExpressionType.Convert } c && c.Operand.NodeType == ExpressionType.Parameter);
            return isParameterAccess ? TryLoadTable(exprType)?.GetFieldByPropertyName(member.Member.Name) : null;
        }

        /// <summary>
        /// True when the filter is an <b>explicit</b> "every row" predicate — the caller-facing synonym for
        /// <c>DeleteAll()</c> / <c>UpdateAll(updates)</c> (SH-H002).
        /// </summary>
        /// <remarks>
        /// <para>Deliberately a <b>one-node</b> test, not a catalogue of the shapes that mean everything.
        /// <see cref="Birko.Data.Expressions.ExpressionNormalizer"/> funcletizes every parameter-free subtree
        /// to a constant first, so <c>x =&gt; true</c>, <c>x =&gt; 1 == 1</c> and <c>x =&gt; capturedFlag</c>
        /// all arrive here as the same <see cref="ConstantExpression"/>. Anything else — including
        /// <c>x =&gt; true || x.A == 1</c>, which the parser also reduces to "everything" — is refused by the
        /// connector guard instead, with a message naming the explicit API.</para>
        /// <para>Enumerating the shapes was tried and rejected: the parser has at least four sites that
        /// legitimately reduce to "everything", so a whitelist here would rot the moment a fifth is added, and
        /// its failure mode is a refused destructive operation on working code.</para>
        /// <para><b>The test itself lives in
        /// <see cref="Birko.Data.Expressions.PredicateScope.IsExplicitAllRows"/></b> and this method forwards
        /// to it. TASK-212 needed the same question answered from <c>Birko.Data.Stores</c>, which cannot see
        /// this class, and briefly had a second identical copy — two definitions of "the caller explicitly
        /// asked for every row", both feeding destructive guards, free to drift apart in exactly the way
        /// § Conventions' one-producer rule describes. This overload is kept because it is public API and
        /// reads naturally beside the rest of the SQL filter surface.</para>
        /// </remarks>
        public static bool IsExplicitAllRows(LambdaExpression? expr)
            => Birko.Data.Expressions.PredicateScope.IsExplicitAllRows(expr);

        /// <summary>
        /// TASK-861 — refuses a destructive write whose <paramref name="filter"/> reduces to every row, for the SQL bulk
        /// stores' read-then-write path (<c>Update(filter, Action)</c>), which emits no conditionless statement for the
        /// connector to refuse. Null and the explicit <c>x =&gt; true</c> are the caller's own checks and pass here.
        /// </summary>
        /// <remarks>
        /// It asks the condition tree that read will execute
        /// (<see cref="Connectors.AbstractConnectorBase.WouldTargetEveryRow"/>), not the expression
        /// (<c>BoundedFilterGuard</c> / <c>PredicateScope</c>), so the read path and the connector's destructive funnels
        /// share one reduction. The expression-level guard does not know an ordering against <c>null</c>, which the
        /// parser folds to always-false: measured on all four SQL providers, <c>Update(x =&gt; !(x.Score &gt; nul), action)</c>
        /// rewrote 6 of 6 rows while <c>Delete</c> with the same filter refused.
        /// </remarks>
        public static void RequireBoundedFilter(LambdaExpression? filter, string operation, string entityName, string allRowsDoor)
        {
            if (filter == null || IsExplicitAllRows(filter)) return;
            if (!Connectors.AbstractConnectorBase.WouldTargetEveryRow(ParseConditionExpression(filter))) return;
            throw new Data.Exceptions.WholeTableWriteException(
                operation, entityName, "every stored entity of that type", allRowsDoor);
        }

        public static IEnumerable<Conditions.Condition> ParseConditionExpression(Expression? expr = null, Conditions.Condition? parent = null, Type? exprType = null)
        {
            if (expr != null)
            {
                RequireTranslatableNode(expr);
                if (expr is LambdaExpression lambdaExpression)
                {
                    var type = lambdaExpression.Parameters?.FirstOrDefault()?.Type;
                    // Canonicalise the predicate once, at the lambda boundary: funcletize parameter-free
                    // subtrees and desugar ternary / ?? into boolean algebra the parser below understands.
                    // TASK-860: GetValueOrDefault becomes ??, a typed Equals ==, a bool XOR != — before the normalizer, so the
                    // ?? it makes of a bool? is desugared with the rest.
                    var rewritten = RewriteTranslatableCalls(lambdaExpression.Body);
                    var body = Birko.Data.Expressions.ExpressionNormalizer.Normalize(rewritten) ?? rewritten;
                    // TASK-863: a boolean compared with a boolean is rewritten into shapes the branches below have.
                    body = RewriteBooleanComparisons(body);
                    // Handle constant boolean body: _ => true means "no filter" (return empty),
                    // _ => false means "match nothing" (return impossible condition 1=0).
                    if (body is ConstantExpression constBody && constBody.Value is bool boolVal)
                    {
                        if (boolVal)
                        {
                            return Array.Empty<Conditions.Condition>();
                        }
                        else
                        {
                            // false → WHERE 1 = 0
                            return new[] { MakeFalseCondition(parent) };
                        }
                    }
                    var res = ParseConditionExpression(body, parent, type);
                    // TASK-859: polarity is known only once the whole tree is built, so the NULL legs are added here.
                    return parent == null ? ApplyCSharpNullSemantics(res) : res;
                }
                else if (expr is UnaryExpression unaryExpression)
                {
                    if (unaryExpression.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs)
                    {
                        // TASK-860: a nullable lift, an enum's integer, a widening is transparent here (the value is
                        // compared, numerically, as it is); a narrowing or lossy conversion is refused. It used to be
                        // dropped: `(int)x.Amount == 10` compared the decimal, and ConvertChecked reached no branch at all.
                        RequireTranslatableConversion(unaryExpression.Operand.Type, unaryExpression.Type);
                        return ParseConditionExpression(unaryExpression.Operand, parent, exprType);
                    }
                    if (unaryExpression.NodeType == ExpressionType.Not)
                    {
                        // Not(HasValue) → IS NULL
                        if (unaryExpression.Operand is MemberExpression notMember)
                        {
                            var hasValueResult = TryParseHasValue(notMember, parent, exprType, isNegated: true);
                            if (hasValueResult != null) return hasValueResult;
                        }
                        // General Not: recurse and set IsNot on the result
                        var notCondition = parent ?? new Conditions.Condition(null, null);
                        notCondition.IsNot = !notCondition.IsNot; // toggle
                        return ParseConditionExpression(unaryExpression.Operand, notCondition, exprType);
                    }
                    // TASK-860: any other unary node (an array's Length, a unary minus as a predicate) used to hand back the
                    // untouched parent condition.
                    if (ContainsParameter(unaryExpression))
                        throw RefuseNode(unaryExpression);
                    if (parent != null)
                    {
                        return new[] { parent };
                    }
                }

                if (expr is BinaryExpression binaryExpression)
                {
                    bool isAnd = false;
                    bool isOR = false;
                    bool isNot = false;
                    ConditionType conditionType = ConditionType.Equal;
                    switch (expr.NodeType)
                    {
                        case ExpressionType.And:
                        case ExpressionType.AndAlso:
                            isAnd = true;
                            isOR = false;
                            break;
                        case ExpressionType.Or:
                        case ExpressionType.OrElse:
                            isAnd = false;
                            isOR = true;
                            break;
                        case ExpressionType.Equal:
                            conditionType = ConditionType.Equal;
                            break;
                        case ExpressionType.NotEqual:
                            conditionType = ConditionType.Equal;
                            isNot = true;
                            break;
                        case ExpressionType.LessThan:
                            conditionType = ConditionType.Less;
                            break;
                        case ExpressionType.LessThanOrEqual:
                            conditionType = ConditionType.LessAndEqual;
                            break;
                        case ExpressionType.GreaterThan:
                            conditionType = ConditionType.Greather;
                            break;
                        case ExpressionType.GreaterThanOrEqual:
                            conditionType = ConditionType.GreatherAndEqual;
                            break;
                        default:
                            // TASK-860: an XOR of a bool? (C#'s lifted ^), an array index, a shift: it used to be parsed as
                            // an `==` of its operands.
                            if (ContainsParameter(binaryExpression))
                                throw RefuseNode(binaryExpression);
                            break;
                    }
                    if ((isAnd || isOR) && UnwrapNullable(binaryExpression.Left.Type) != typeof(bool) && ContainsParameter(binaryExpression))
                        throw RefuseNode(binaryExpression);

                    if (isAnd || isOR)
                    {
                        // Fast-path: resolve literal/closure bools without allocating Condition objects.
                        // Only falls back to full parsing when the expression references a lambda parameter.
                        var leftBool = TryGetLiteralBool(binaryExpression.Left);
                        var rightBool = TryGetLiteralBool(binaryExpression.Right);

                        if (leftBool.HasValue && rightBool.HasValue)
                        {
                            bool r = isAnd ? (leftBool.Value && rightBool.Value) : (leftBool.Value || rightBool.Value);
                            return r ? Array.Empty<Conditions.Condition>() : new[] { MakeFalseCondition(parent) };
                        }

                        Conditions.Condition? leftCond = null, rightCond = null;
                        bool leftIsConst = leftBool.HasValue, leftVal = leftBool ?? false;
                        bool rightIsConst = rightBool.HasValue, rightVal = rightBool ?? false;

                        if (!leftIsConst)
                        {
                            leftCond = new Conditions.Condition(null, null);
                            ParseConditionExpression(binaryExpression.Left, leftCond, exprType);
                            leftIsConst = IsConstantBoolCondition(leftCond, out leftVal);
                        }
                        if (!rightIsConst)
                        {
                            rightCond = new Conditions.Condition(null, null);
                            ParseConditionExpression(binaryExpression.Right, rightCond, exprType);
                            rightIsConst = IsConstantBoolCondition(rightCond, out rightVal);
                        }

                        if (leftIsConst && rightIsConst)
                        {
                            bool r = isAnd ? (leftVal && rightVal) : (leftVal || rightVal);
                            return r ? Array.Empty<Conditions.Condition>() : new[] { MakeFalseCondition(parent) };
                        }
                        if (leftIsConst)
                        {
                            if (isAnd && leftVal) return ReturnSingleSubCondition(parent, rightCond!);
                            if (isAnd && !leftVal) return new[] { MakeFalseCondition(parent) };
                            if (isOR && leftVal) return Array.Empty<Conditions.Condition>();
                            if (isOR && !leftVal) return ReturnSingleSubCondition(parent, rightCond!);
                        }
                        if (rightIsConst)
                        {
                            if (isAnd && rightVal) return ReturnSingleSubCondition(parent, leftCond!);
                            if (isAnd && !rightVal) return new[] { MakeFalseCondition(parent) };
                            if (isOR && rightVal) return Array.Empty<Conditions.Condition>();
                            if (isOR && !rightVal) return ReturnSingleSubCondition(parent, leftCond!);
                        }

                        if (parent != null)
                        {
                            // Transfer the OR/AND flag to the parent so the SQL generator
                            // knows to join subconditions with OR instead of AND.
                            parent.IsOr = isOR;
                            var subs = parent.SubConditions as List<Condition> ?? new List<Condition>(parent.SubConditions ?? []);
                            subs.Add(leftCond!);
                            subs.Add(rightCond!);
                            parent.SubConditions = subs;
                            return new[] { parent };
                        }
                        else
                        {
                            return new[] { new Conditions.Condition(null, null)
                            {
                                IsOr = isOR,
                                Type = conditionType,
                                IsNot = isNot,
                                SubConditions = new List<Condition> { leftCond!, rightCond! },
                            }};
                        }
                    }
                    else
                    {
                        // If neither side references a lambda parameter, the whole comparison
                        // is a constant expression (e.g. `status == null`, `hasConfig == true`).
                        // Evaluate it at parse time and return a constant bool condition.
                        if (!ContainsParameter(binaryExpression.Left) && !ContainsParameter(binaryExpression.Right))
                        {
                            try
                            {
                                var constVal = EvaluateExpression(binaryExpression);
                                if (constVal is bool boolVal)
                                {
                                    if (boolVal)
                                        return Array.Empty<Conditions.Condition>();
                                    else
                                        return new[] { MakeFalseCondition(parent) };
                                }
                            }
                            catch { /* fall through to normal handling */ }
                        }

                        // TASK-860: each operand that reads the row is one the parser translates, or the comparison is
                        // refused by name before anything is built. Before, an unknown call, member or conversion was
                        // recursed into and its column kept: `x.Code.Substring(1) == "bc"` compared Code with 'bc'.
                        RequireTranslatableComparand(binaryExpression.Left, exprType);
                        RequireTranslatableComparand(binaryExpression.Right, exprType);

                        // Value-expression operand — column arithmetic (x.A + x.B > 5, x.Price * 2 >= 10),
                        // null-coalescing ((x.Score ?? 0) > 5) or a value-position ternary
                        // ((x.Vip ? x.Premium : x.Score) > 100, i.e. CASE in WHERE): render the value side(s)
                        // to a raw SQL fragment and compare. Without this these nodes fall through the
                        // comparison switch and are mis-parsed. (Boolean-typed ?:/?? were already desugared
                        // to AND/OR by the normalizer and never reach here.)
                        var valLeft = UnwrapConvert(binaryExpression.Left);
                        var valRight = UnwrapConvert(binaryExpression.Right);

                        // TASK-574: both operands read the row (`x.A == x.B`, `x.Start.Date < x.End.Date`). The
                        // fall-through below parses both sides into ONE condition, so the right operand's name
                        // overwrote the left's and no value was bound: `B = NULL`, zero rows, no error. Route the
                        // pair through the column-vs-column builder instead, which renders both sides.
                        if (ContainsParameter(valLeft) && ContainsParameter(valRight))
                        {
                            var columnCondition = BuildColumnComparison(valLeft, valRight, expr.NodeType, conditionType, isNot, isOR, exprType, binaryExpression.Left.Type);
                            if (parent != null)
                            {
                                var columnSubs = parent.SubConditions as List<Condition> ?? new List<Condition>(parent.SubConditions ?? []);
                                columnSubs.Add(columnCondition);
                                parent.SubConditions = columnSubs;
                                return new[] { parent };
                            }
                            return new[] { columnCondition };
                        }

                        if (IsValueExpressionOperand(valLeft) || IsValueExpressionOperand(valRight))
                        {
                            var valueCondition = BuildValueComparison(valLeft, valRight, conditionType, isNot, isOR, exprType, binaryExpression.Left.Type);
                            if (parent != null)
                            {
                                var valueSubs = parent.SubConditions as List<Condition> ?? new List<Condition>(parent.SubConditions ?? []);
                                valueSubs.Add(valueCondition);
                                parent.SubConditions = valueSubs;
                                return new[] { parent };
                            }
                            return new[] { valueCondition };
                        }

                        // `x.Col.Date <op> <value>` — rewrite to a half-open range on the RAW column
                        // instead of comparing DATE(col) against a bound DateTime. See
                        // TryBuildDateTruncatedComparison for why the DATE() form silently matches
                        // nothing. Only fires when exactly one side is a column `.Date` and the other
                        // evaluates to a constant; column-vs-column keeps the old DATE() path.
                        var dateRange = TryBuildDateTruncatedComparison(
                            binaryExpression.Left, binaryExpression.Right, expr.NodeType, exprType);
                        if (dateRange != null)
                        {
                            if (parent != null)
                            {
                                // Nest it rather than merging into the parent: the `!=` arm carries its
                                // own IsOr=true, and ReturnSingleSubCondition overwrote that with the
                                // enclosing node's flag until TASK-866 — turning `col < d OR col >= d+1` into an
                                // unsatisfiable AND that matches zero rows. Same shape as the
                                // value-comparison branch above.
                                var dateSubs = parent.SubConditions as List<Condition>
                                    ?? new List<Condition>(parent.SubConditions ?? []);
                                dateSubs.Add(dateRange);
                                parent.SubConditions = dateSubs;
                                return new[] { parent };
                            }
                            return new[] { dateRange };
                        }

                        // SH-M123 (TASK-153): the condition is rendered `column op value`, whichever side each came
                        // from, so a value on the LEFT must flip the operator. `25 > x.Age` rendered `Age > 25`, the
                        // complement's neighbour. The value-expression and `.Date` paths above already flipped.
                        var comparedLeft = binaryExpression.Left;
                        var comparedRight = binaryExpression.Right;
                        if (!ContainsParameter(comparedLeft) && ContainsParameter(comparedRight))
                        {
                            (comparedLeft, comparedRight) = (comparedRight, comparedLeft);
                            conditionType = FlipComparison(conditionType);
                        }
                        var basecondition = new Conditions.Condition(null, null)
                        {
                            IsOr = isOR,
                            Type = conditionType,
                            IsNot = isNot,
                        };
                        var left = ParseConditionExpression(comparedLeft, basecondition, exprType);
                        var right = ParseConditionExpression(comparedRight, basecondition, exprType);
                        StoreComparedValues(basecondition, binaryExpression, exprType);
                        if (basecondition.SubConditions?.Any() != true && IsPlainColumnOperand(comparedLeft, exprType))
                            MarkNullableComparison(basecondition, comparedLeft, exprType);
                        if (parent != null)
                        {
                            var subs = parent.SubConditions as List<Condition> ?? new List<Condition>(parent.SubConditions ?? []);
                            subs.Add(basecondition);
                            parent.SubConditions = subs;
                            return new[] { parent };
                        }
                        else
                        {
                            return new[] { basecondition };
                        }
                    }
                }
                else if (expr is MethodCallExpression methodExpression)
                {
                    var condition = parent ?? new Conditions.Condition(null, null);

                    // SH-M127 (TASK-153): `string.IsNullOrEmpty(x.Col)` matched no case below, so the column became the
                    // Name with no value bound: `Col = NULL`, no rows, and its negation no rows either.
                    if (methodExpression.Method.Name == "IsNullOrEmpty"
                        && methodExpression.Method.DeclaringType == typeof(string)
                        && methodExpression.Arguments.Count == 1
                        && ContainsParameter(methodExpression.Arguments[0]))
                    {
                        var probe = new Conditions.Condition(null, null);
                        ParseConditionExpression(methodExpression.Arguments[0], probe, exprType);
                        if (string.IsNullOrEmpty(probe.Name) || probe.SubConditions?.Any() == true)
                            throw new NotSupportedException(
                                $"Cannot translate '{methodExpression}' to SQL: its argument is not a column.");
                        // Nested in the condition, so the condition's own IsNot (from an enclosing `!`) negates the
                        // whole OR, and its IsOr keeps joining it to its sibling: NOT (Col IS NULL OR Col = '').
                        var subs = condition.SubConditions as List<Condition> ?? new List<Condition>(condition.SubConditions ?? []);
                        subs.Add(new Conditions.Condition(null, null)
                        {
                            IsOr = true,
                            SubConditions = new List<Condition>
                            {
                                new Conditions.Condition(probe.Name, null, ConditionType.IsNull),
                                new Conditions.Condition(probe.Name, new object[] { string.Empty }, ConditionType.Equal),
                            },
                        });
                        condition.SubConditions = subs;
                        return new[] { condition };
                    }

                    // SH-M125 (TASK-153): `x.Name.StartsWith(x.Prefix)` parsed both columns into one condition, the
                    // object overwriting the pattern, with no value bound: `Name LIKE NULL`, no rows. Building a
                    // pattern from a column needs string concatenation, which no provider spells the same way.
                    if (methodExpression.Method.DeclaringType == typeof(string)
                        && methodExpression.Method.Name is "StartsWith" or "EndsWith" or "Contains"
                        && methodExpression.Object != null && ContainsParameter(methodExpression.Object)
                        && methodExpression.Arguments.Count > 0 && ContainsParameter(methodExpression.Arguments[0]))
                    {
                        throw new NotSupportedException(
                            $"Cannot translate '{methodExpression}' to SQL: the pattern is a column, and building a LIKE "
                            + "pattern from a column has no portable SQL form. Compare against a value instead.");
                    }

                    // TASK-860: the parser's default for a call that reads the row is refusal, not pass-through.
                    if (ContainsParameter(methodExpression))
                        RequireTranslatableCall(methodExpression);

                    // TASK-869: an explicit Ordinal / OrdinalIgnoreCase is rendered exactly by the provider's dialect.
                    if (methodExpression.Method.DeclaringType == typeof(string) && IsStringComparisonEquals(methodExpression.Method)
                        && methodExpression.Method.IsStatic)
                        return new[] { ParseStringEquals(methodExpression, condition, exprType) };

                    switch (methodExpression.Method.Name)
                    {
                        case "StartsWith":
                            condition.Type = ConditionType.StartsWith;
                            break;
                        case "EndsWith":
                            condition.Type = ConditionType.EndsWith;
                            break;
                        case "Contains":
                            condition.Type = methodExpression.Method.DeclaringType?.Name == "String"
                                ? ConditionType.Like
                                : ConditionType.In;
                            break;
                        case "ToLower":
                        case "ToLowerInvariant":
                            {
                                // Unwrap ToLower: recurse into the Object, then wrap column name with LOWER()
                                if (methodExpression.Object != null)
                                {
                                    var inner = new Conditions.Condition(null, null);
                                    ParseConditionExpression(methodExpression.Object, inner, exprType);
                                    if (!string.IsNullOrEmpty(inner.Name))
                                    {
                                        condition.Name = $"LOWER({inner.Name})";
                                    }
                                }
                                return new[] { condition };
                            }
                        case "ToUpper":
                        case "ToUpperInvariant":
                            {
                                if (methodExpression.Object != null)
                                {
                                    var inner = new Conditions.Condition(null, null);
                                    ParseConditionExpression(methodExpression.Object, inner, exprType);
                                    if (!string.IsNullOrEmpty(inner.Name))
                                    {
                                        condition.Name = $"UPPER({inner.Name})";
                                    }
                                }
                                return new[] { condition };
                            }
                    }
                    // TASK-858: the set-Contains operand's resolution, shared with its IS NULL leg below.
                    FragmentResolution? fragmentResolution = null;
                    if (methodExpression.Arguments.Any())
                    {
                        // For the string pattern methods (Contains/StartsWith/EndsWith on
                        // System.String) only the FIRST argument is the search pattern. The
                        // culture-aware overloads carry extra arguments — a StringComparison,
                        // a bool ignoreCase, a CultureInfo — that map to no SQL operand. Feeding
                        // them into the loop overwrote the pattern with the enum/flag value, so
                        // e.g. Title.Contains(query, StringComparison.OrdinalIgnoreCase) produced
                        // `Title LIKE '%5%'`. Case-insensitivity is delegated to the column's DB
                        // collation (SQLite LIKE is already case-insensitive for ASCII); the
                        // comparison argument is intentionally ignored when building the pattern.
                        var methodName = methodExpression.Method.Name;
                        var isStringPatternMethod = methodName == "StartsWith"
                            || methodName == "EndsWith"
                            || (methodName == "Contains" && methodExpression.Method.DeclaringType?.Name == "String");
                        if (isStringPatternMethod)
                        {
                            ParseConditionExpression(methodExpression.Arguments[0], condition, exprType);
                        }
                        else
                        {
                            foreach (var arg in methodExpression.Arguments)
                            {
                                // Same class of problem as the string-pattern arguments above: an
                                // overload-disambiguating argument that maps to no SQL operand must not
                                // be fed to the parser. On .NET 9+ an ARRAY `set.Contains(x.Col)` binds
                                // to MemoryExtensions.Contains(ReadOnlySpan<T>, T, IEqualityComparer<T>?)
                                // whenever T is not IEquatable<T> — true for every enum and nullable
                                // enum. That trailing `null` comparer took the constant-null path and
                                // flipped the whole condition to IS NULL, so `statuses.Contains(x.Status)`
                                // matched rows with a NULL column — none, silently. Guid/int/string sets
                                // ARE IEquatable and bind the 2-argument overload, which is why the
                                // canonical N+1 batch pattern never exposed it. Measured in a consumer
                                // (Symbio TASK-249/TASK-254): 0 rows against 21 matching.
                                if (IsNonOperandArgument(arg))
                                {
                                    continue;
                                }
                                // TASK-213: the VALUE operand of a set `Contains` is a value expression, not a
                                // nested predicate. Recursing into it made `set.Contains(x.Amount + 1)` take the
                                // binary-comparison path, which fabricated a SUBcondition (`Amount = 1`) on this
                                // very condition — and because the renderer branches on SubConditions before it
                                // looks at Type, the `In` and its Values were then ignored entirely and only the
                                // fabricated equality was emitted. Not a broken predicate: a DIFFERENT one, with
                                // no exception and no log entry. Measured on SQLite,
                                // `SomeIds.Contains(x.Amount + 1)` returned 1 row where the oracle says 0, and
                                // the negated form 3 where it says 4.
                                //
                                // Resolved the same way a comparison resolves its column side
                                // (see BuildValueComparison): the parameter-referencing operand becomes this
                                // condition's Name as a raw SQL fragment. RenderValueFragment covers arithmetic,
                                // COALESCE, CASE, `.Value` unwrap and inlined constants, and THROWS
                                // NotSupportedException for anything it cannot faithfully translate — so an
                                // operand this parser cannot express now fails loud instead of silently becoming
                                // another predicate (§ SH-H037).
                                //
                                // Gated on "not a plain resolvable column" purely to leave every shape that works
                                // today on its existing path, byte-for-byte. On a plain column the two agree
                                // anyway: both resolve through ResolveColumnName(exprType, name, true).
                                if (condition.Type == ConditionType.In
                                    && ContainsParameter(arg)
                                    && !IsPlainColumnOperand(arg, exprType))
                                {
                                    fragmentResolution = new FragmentResolution();
                                    condition.Name = RenderValueFragment(arg, exprType, fragmentResolution);
                                    condition.NameFragment = ComparedFragment(arg, arg.Type, exprType, fragmentResolution);
                                    continue;
                                }
                                ParseConditionExpression(arg, condition, exprType);
                            }
                        }
                    }
                    if (methodExpression.Object != null)
                    {
                        ParseConditionExpression(methodExpression.Object, condition, exprType);
                    }
                    if (methodExpression.Method.DeclaringType == typeof(string) && methodExpression.Object != null
                        && methodExpression.Method.Name is "StartsWith" or "EndsWith" or "Contains"
                        && TranslatableComparison(methodExpression) is { } comparison)
                        WithComparison(condition, comparison, methodExpression.Method.Name);
                    if (condition.Type == ConditionType.In)
                    {
                        // TASK-606: the list is bound in the column's stored form, as a comparison's value is (TASK-596).
                        // The column is an argument (static Enumerable / MemoryExtensions.Contains) or the Object
                        // (List<T>.Contains); the other side is the list.
                        StoreColumnValues(condition, exprType, methodExpression.Arguments.Concat(new[] { methodExpression.Object }).ToArray());
                        if (!condition.IsField && condition.SubConditions?.Any() != true)
                        {
                            // Not for a call with no row operand: an array's implicit span conversion is parsed through
                            // here too, as the list argument of the Contains that owns the operand.
                            var operand = methodExpression.Arguments.Concat(new[] { methodExpression.Object })
                                .FirstOrDefault(e => e != null && !IsNonOperandArgument(e) && ContainsParameter(e));
                            if (operand != null)
                                MarkNullableIn(condition, operand, exprType, fragmentResolution ?? new FragmentResolution());
                        }
                    }
                    return new[] { condition };
                }
                // Bare HasValue access at top level: x.NullableProp.HasValue → Prop IS NOT NULL
                if (expr is MemberExpression topMember)
                {
                    var hasValueResult = TryParseHasValue(topMember, parent, exprType, isNegated: false);
                    if (hasValueResult != null) return hasValueResult;
                }
                // Bare boolean member access: s.IsActive → "IsActive" = 1
                // The C# expression tree represents `s.IsActive` as a MemberExpression
                // without a parent comparison. Convert to an explicit `Prop = true` condition.
                if (expr is MemberExpression bareBoolMember
                    && bareBoolMember.Type == typeof(bool)
                    && exprType != null
                    && bareBoolMember.Member.ReflectedType?.IsAssignableFrom(exprType) == true
                    && (bareBoolMember.Expression?.NodeType == ExpressionType.Parameter
                        || (bareBoolMember.Expression is UnaryExpression bareBoolConvert
                            && bareBoolConvert.NodeType == ExpressionType.Convert
                            && bareBoolConvert.Operand.NodeType == ExpressionType.Parameter)))
                {
                    var condition = parent ?? new Conditions.Condition(null, null);
                    condition.Name = ResolveColumnName(exprType, bareBoolMember.Member.Name, true) ?? bareBoolMember.Member.Name;
                    condition.Type = ConditionType.Equal;
                    condition.Values = new object[] { true };
                    return new[] { condition };
                }
                // TASK-860: a bool member over the row that is no column (`x => x.Sub.Flag`) produced no condition, which
                // reads as every row.
                if (parent == null && expr is MemberExpression unclaimedMember && ContainsParameter(unclaimedMember))
                    throw RefuseMember(unclaimedMember);

                if (parent != null)
                {
                    if (expr is ConstantExpression || expr is MethodCallExpression)
                    {
                        if (parent.Type == ConditionType.In
                            && EvaluateExpression(expr) is IEnumerable list && list is not string)
                        {
                            // TASK-859: the list is kept whole, a null included. In C# a null in the list matches a
                            // NULL column, so MarkNullableIn turns it into the column's IS NULL rather than dropping it
                            // (which matched no NULL row) or binding it (`NOT IN (5, NULL)` matches no row at all).
                            parent.Values = list.Cast<object?>().ToArray();
                        }
                        else if (InvokeExpression(expr)?.Where(x => x != null).ToArray() is { Length: > 0 } materialized)
                        {
                            parent.Values = materialized;
                        }
                        else if (parent.Type == ConditionType.In)
                        {
                            // An empty (or null) collection in an IN predicate means "matches nothing" — NOT "the
                            // column is null". Keep the condition an In with no values; InConditionStrategy renders
                            // that as the always-false / always-true constant (an empty NOT IN matches everything).
                            parent.Values = System.Array.Empty<object>();
                        }
                        else
                        {
                            // Not an IN: a null/empty constant is a genuine `= null` → IS NULL; an ordering against
                            // null is constant (TASK-859).
                            ComparedWithNull(parent);
                        }
                    }
                    else if (expr is NewArrayExpression arrayExpresion)
                    {
                        foreach (var arg in arrayExpresion.Expressions)
                        {
                            ParseConditionExpression(arg, parent, exprType);
                        }
                    }
                    else if (expr is MemberExpression memberExpression)
                    {
                        // Non-parameter member access (closure variable, e.g. configId.Value,
                        // filter.Status, local variable) — evaluate as a constant value.
                        // Must be checked BEFORE column-resolution logic to avoid treating
                        // closure fields as DB columns.
                        if (!ContainsParameter(memberExpression))
                        {
                            // Special case: HasValue on closure nullable → evaluate as bool constant
                            if (memberExpression.Member.Name == "HasValue"
                                && memberExpression.Member.ReflectedType != null
                                && Nullable.GetUnderlyingType(memberExpression.Member.ReflectedType) != null)
                            {
                                var boolVal = EvaluateExpression(memberExpression);
                                parent.Values = new[] { boolVal };
                                return new[] { parent };
                            }

                            var value = EvaluateExpression(memberExpression);
                            // A closure variable that evaluates to null must become IS NULL, exactly as a
                            // literal `== null` does (see the ConstantExpression branch above). Otherwise
                            // `x.Col == nullableVar` (var == null) emits `Col = NULL`, which is always
                            // UNKNOWN in SQL → zero rows. For `!=`, IsNot is already set, so this yields
                            // IS NOT NULL — symmetric with the literal-null path. An ordering against null is constant (TASK-859).
                            if (value == null)
                            {
                                ComparedWithNull(parent);
                                return new[] { parent };
                            }
                            parent.Values = (value is not string && value is IEnumerable enumerable)
                                ? (enumerable as object[] ?? enumerable.Cast<object>().ToArray())
                                : new[] { value };
                            return new[] { parent };
                        }

                        // Bare HasValue access on parameter: x.NullableProp.HasValue → Prop IS NOT NULL
                        var hasValueResult = TryParseHasValue(memberExpression, parent, exprType, isNegated: false);
                        if (hasValueResult != null) return hasValueResult;

                        // Handle DateTime.Date property: x.OpenedAt.Date → DATE(column)
                        if (memberExpression.Member.Name == "Date"
                            && memberExpression.Member.DeclaringType == typeof(DateTime)
                            && memberExpression.Expression is MemberExpression innerDateMember)
                        {
                            if (ContainsParameter(innerDateMember))
                            {
                                var inner = new Condition(null, null);
                                ParseConditionExpression(innerDateMember, inner, exprType);
                                if (!string.IsNullOrEmpty(inner.Name))
                                {
                                    parent.Name = $"DATE({inner.Name})";
                                    return new[] { parent };
                                }
                                // TASK-860: a .Date of something that is no column was evaluated as a constant from the row.
                                throw RefuseMember(memberExpression);
                            }
                            // Right-hand side: evaluate as constant
                            var dateValue = EvaluateExpression(memberExpression);
                            parent.Values = new[] { dateValue };
                            return Array.Empty<Condition>();
                        }

                        string name = string.Empty;
                        if (
                            exprType != null
                            && memberExpression.Expression != null
                            && memberExpression.Expression.NodeType == ExpressionType.MemberAccess
                            && memberExpression.Member.ReflectedType != null
                            && Nullable.GetUnderlyingType(memberExpression.Member.ReflectedType) != null
                        )
                        {
                            var member = new Condition(null, null);
                            ParseConditionExpression(memberExpression.Expression, member, exprType);
                            name = member.Name ?? string.Empty;
                        }
                        // Check for direct parameter access or Convert(param, Interface) — the C# compiler
                        // generates Convert when accessing interface properties through generic constraints
                        var isParameterAccess = memberExpression.Expression?.NodeType == ExpressionType.Parameter
                            || (memberExpression.Expression is UnaryExpression convertExpr
                                && convertExpr.NodeType == ExpressionType.Convert
                                && convertExpr.Operand.NodeType == ExpressionType.Parameter);
                        if (
                            exprType != null
                            && memberExpression.Expression != null
                            && memberExpression.Member.ReflectedType != null
                            && memberExpression.Member.ReflectedType.IsAssignableFrom(exprType)
                            && isParameterAccess
                        )
                        {
                            name = ResolveColumnName(exprType, memberExpression.Member.Name, true) ?? string.Empty;
                        }
                        if (string.IsNullOrEmpty(name))
                        {
                            if ((memberExpression.Expression is ConstantExpression
                                || (memberExpression.Expression != null && !ContainsParameter(memberExpression))))
                            {
                                // Closure field or nested member access — evaluate as constant
                                var value = EvaluateExpression(memberExpression);
                                parent.Values = (value != null && value is not string && value is IEnumerable enumerable)
                                    ? (enumerable as object[] ?? enumerable.Cast<object>().ToArray())
                                    : new[] { value };
                            }
                            else if (ContainsParameter(memberExpression))
                            {
                                // TASK-860: a member over the row that is no column (`x.Code.Length`, `x.At.Year`) was
                                // evaluated with the row as null and became IS NULL, or a condition with no name.
                                throw RefuseMember(memberExpression);
                            }
                            else
                            {
                                IEnumerable<object>? vals = InvokeExpression(expr);
                                var materialized = vals?.Where(x => x != null).ToArray();
                                if (materialized?.Length > 0)
                                {
                                    parent.Values = materialized;
                                }
                                else
                                {
                                    parent.Type = ConditionType.IsNull;
                                }
                            }
                        }
                        else
                        {
                            parent.Name = name;
                        }
                    }
                }
            }
            return Array.Empty<Condition>();
        }

        /// <summary>
        /// True for method-call arguments that carry no SQL operand — equality/ordering comparers and
        /// culture/comparison selectors. They exist only to pin down a CLR overload; handing one to the
        /// condition parser corrupts the condition (a null comparer becomes IS NULL, a StringComparison
        /// becomes the LIKE pattern). Note <see cref="IEqualityComparer{T}"/> does not implement the
        /// non-generic <see cref="IEqualityComparer"/>, so both have to be checked.
        /// <para>
        /// A non-null comparer (e.g. <c>set.Contains(x.Name, StringComparer.OrdinalIgnoreCase)</c>) is
        /// skipped too: its comparison SEMANTICS are delegated to the column's DB collation, exactly as
        /// the <c>StringComparison</c> overloads of the string pattern methods already are. Honouring the
        /// operand while ignoring the comparer is the closest correct translation available; the
        /// alternative (parsing it as a value) produced a condition that matched the wrong rows.
        /// </para>
        /// </summary>
        private static bool IsNonOperandArgument(Expression arg)
        {
            var type = arg.Type;
            if (type == typeof(StringComparison) || type == typeof(System.Globalization.CultureInfo))
                return true;
            if (typeof(IEqualityComparer).IsAssignableFrom(type) || typeof(IComparer).IsAssignableFrom(type))
                return true;
            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(IEqualityComparer<>) || definition == typeof(IComparer<>))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves a C# property name to its SQL column/field select name using LoadTable + GetFieldByPropertyName,
        /// with fallback to ResolveFieldSelectName delegate (for views).
        /// Returns null if the property cannot be resolved.
        /// </summary>
        private static string? ResolveColumnName(Type exprType, string propertyName, bool withTableName)
        {
            var scoped = _scopedFieldResolver.Value?.Invoke(exprType, propertyName, withTableName);
            if (scoped != null) return scoped;

            var table = TryLoadTable(exprType);
            if (table != null)
            {
                var field = table.GetFieldByPropertyName(propertyName);
                if (field != null) return field.GetSelectName(withTableName);
            }
            return ResolveFieldSelectName?.Invoke(exprType, propertyName, withTableName);
        }

        /// <summary>
        /// Tries to parse a HasValue member access on a nullable property into an IS NULL / IS NOT NULL condition.
        /// Returns null if the expression is not a HasValue access.
        /// </summary>
        /// <param name="member">The member expression to check.</param>
        /// <param name="parent">Parent condition to populate, or null to create a new one.</param>
        /// <param name="exprType">The lambda parameter type for column resolution.</param>
        /// <param name="isNegated">True when inside a Not() wrapper (HasValue negated → IS NULL), false for bare HasValue (→ IS NOT NULL).</param>
        private static IEnumerable<Condition>? TryParseHasValue(MemberExpression member, Condition? parent, Type? exprType, bool isNegated)
        {
            if (member.Member.Name != "HasValue"
                || member.Member.ReflectedType == null
                || Nullable.GetUnderlyingType(member.Member.ReflectedType) == null
                || member.Expression is not MemberExpression innerMember)
                return null;

            var condition = parent ?? new Condition(null, null);
            condition.Type = ConditionType.IsNull;
            if (!isNegated)
                condition.IsNot = !condition.IsNot;
            var inner = new Condition(null, null);
            ParseConditionExpression(innerMember, inner, exprType);
            condition.Name = inner.Name;
            return new[] { condition };
        }

        /// <summary>
        /// The expression node kinds <see cref="ParseConditionExpression"/> has a branch for. Anything else
        /// is <b>refused</b> rather than parsed into nothing.
        /// </summary>
        /// <remarks>
        /// <para><b>SH-H021 + SH-H026 / TASK-308 — one root cause with two faces.</b>
        /// <see cref="ParseConditionExpression"/> dispatches on lambda, unary, binary, method-call and
        /// member nodes, in three independent <c>if</c> groups, and ends in
        /// <c>return Array.Empty&lt;Condition&gt;()</c>. So a node kind none of them claims —
        /// <see cref="TypeBinaryExpression"/> (<c>x.Payload is string</c>),
        /// <see cref="InvocationExpression"/> (<c>x =&gt; pred(x)</c>), <see cref="NewExpression"/> —
        /// produced <b>no conditions and no complaint</b>, which
        /// <c>AbstractConnectorBase.AddWhere</c> renders as no <c>WHERE</c> at all: indistinguishable
        /// from <c>_ =&gt; true</c>. Measured on SQLite, 3 rows: a top-level <c>x.Payload is string</c>
        /// read <b>3 of 3 rows</b>, and as an <c>||</c> operand it made
        /// <c>(x.Payload is string) || x.Amount == 10</c> read 3 of 3 too, because
        /// <see cref="IsConstantBoolCondition"/> reads the untouched condition as constant
        /// <c>true</c>. As an <c>&amp;&amp;</c> operand the term was silently dropped instead.</para>
        /// <para><b>Why the guard is here and not in <see cref="IsConstantBoolCondition"/>.</b> That
        /// predicate's <c>Values == null → true</c> branch has a <b>legitimate</b> reachability, measured:
        /// <c>(x.A == 1 || true) &amp;&amp; x.B == 2</c> leaves exactly that state for its left operand and
        /// <c>true</c> is the right answer there. "Nothing was parsed" is therefore overloaded, and the two
        /// cases have to be told apart at the point the ambiguity is created — the unclaimed node — rather
        /// than merged at the point it is read.</para>
        /// <para><b>Why refuse rather than degrade.</b> § SH-H037: a mapper that cannot express something
        /// refuses; it never drops it quietly. The opt-out exists and is the same one every other
        /// backend offers — write a predicate the translator supports, or ask for every row explicitly
        /// through <c>Read()</c> / <c>DeleteAll()</c>. <see cref="NotSupportedException"/> deliberately
        /// matches what <c>ElasticSearch.ParseFilterQuery</c> throws for the same predicate (TASK-268), so
        /// one <c>catch</c> selects an untranslatable filter on either backend.</para>
        /// <para><b>The destructive half was already covered and this does not replace it.</b> Measured:
        /// <c>DeleteAsync</c> on both shapes already threw <c>WholeTableWriteException</c> from
        /// SH-H002's <c>AddRequiredWhere</c>, leaving 3 of 3 rows. This closes the <i>read</i>-path wrong
        /// answer those tasks left open — and it now fires earlier, so the refusal names the predicate
        /// rather than the table.</para>
        /// </remarks>
        /// <remarks>
        /// <c>ConstantExpression</c> and <c>ParameterExpression</c> are on the list <b>permissively</b>:
        /// neither is known to reach here (a constant operand is resolved by <c>TryGetLiteralBool</c>
        /// first, and the lambda branch handles a constant body), but the list errs toward accepting,
        /// because a false refusal breaks working code and is worse than the hole — the asymmetry
        /// <c>PredicateScope</c> records. Adding a kind here is safe; removing one is not.
        /// </remarks>
        private static void RequireTranslatableNode(Expression expr)
        {
            if (expr is LambdaExpression
                || expr is UnaryExpression
                || expr is BinaryExpression
                || expr is MethodCallExpression
                || expr is MemberExpression
                || expr is ConstantExpression
                || expr is ParameterExpression)
            {
                return;
            }

            // ⚠ The node's TYPE only — never expr.ToString(). A rendered expression tree interpolates the
            // values a closure captured, and this message travels into logs and error responses. Same
            // reasoning as the identifier family's refusals: the diagnosis needs the shape, not the data.
            throw new NotSupportedException(
                $"The filter cannot be translated to SQL: it contains a {expr.NodeType} node "
                + $"({expr.GetType().Name}) that the condition parser has no rule for. "
                + "Before this was refused it produced no WHERE clause, which reads as every row. "
                + "Rewrite the predicate using comparisons, boolean operators, member access and the "
                + "supported method calls, or ask for every row explicitly with Read() / DeleteAll().");
        }

        /// <summary>
        /// Checks whether a parsed condition represents a bare constant boolean
        /// (Name is null/empty, single boolean value, default Equal type).
        /// Produced when the expression tree contains a literal true/false operand
        /// such as in <c>_ =&gt; true &amp;&amp; _.DeletedAt == null</c>.
        /// </summary>
        private static bool IsConstantBoolCondition(Conditions.Condition condition, out bool value)
        {
            value = false;
            if (!string.IsNullOrEmpty(condition.Name) || condition.SubConditions?.Any() == true)
                return false;
            if (condition.Values is IEnumerable<object> vals)
            {
                using var en = vals.GetEnumerator();
                if (en.MoveNext() && en.Current is bool b && !en.MoveNext())
                {
                    value = condition.IsNot ? !b : b;
                    return true;
                }
            }
            // An empty condition (no Name, no SubConditions, no Values) means the
            // sub-expression returned Array.Empty (constant true) and the parent
            // was never populated. Treat it as constant true.
            if (condition.Values == null)
            {
                value = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Creates the always-false condition (<c>1 = 0</c>) for short-circuiting AND with false.
        /// </summary>
        /// <remarks>
        /// TASK-861: it is the empty <c>IN</c>, the one always-false leaf
        /// <see cref="Connectors.AbstractConnectorBase.ConstantTruth"/> knows, so a negation the parser has already put
        /// on <paramref name="parent"/> (<c>!(x.A &gt; 0 &amp;&amp; off)</c>) reduces to always-true and a destructive
        /// statement refuses it. It used to be <c>1 = @p</c> bound to 0, which nothing reduced: under that negation it
        /// rendered a WHERE that is true of every row. The name is never rendered.
        /// </remarks>
        private static Conditions.Condition MakeFalseCondition(Conditions.Condition? parent)
        {
            var c = parent ?? new Conditions.Condition(null, null);
            c.Name = "1";
            c.Values = Array.Empty<object>();
            c.Type = ConditionType.In;
            return c;
        }

        /// <summary>
        /// Unwraps a single surviving subcondition when the other operand of AND/OR was a constant.
        /// Transfers its content to the parent if present, or returns it standalone.
        /// </summary>
        /// <remarks>
        /// <para><b>SH-H022 / TASK-308 — the parent's <c>IsNot</c> is COMBINED, not assigned.</b> The
        /// <c>Not</c> branch toggles <c>IsNot</c> on the very object it hands down as
        /// <paramref name="parent"/>, so an assignment here silently discards an enclosing negation.
        /// Measured on SQLite before the fix: <c>x =&gt; !(x.Amount == 10 &amp;&amp; trueFlag)</c> rendered
        /// <c>WHERE Amount = @p</c> where the control <c>x =&gt; !(x.Amount == 10)</c> rendered
        /// <c>WHERE NOT (Amount = @p)</c> — so the read returned <b>1 row instead of 2</b>, the exact
        /// complement, and <c>DeleteAsync</c> threw nothing and destroyed that complement. The clause is
        /// non-empty, so SH-H002's <c>AddRequiredWhere</c> has nothing to refuse: this was the one finding
        /// in its area whose destructive path was unguarded.</para>
        /// <para>XOR rather than <c>||</c>: two negations cancel. <c>!(x.A != 1 &amp;&amp; true)</c> is
        /// <c>x.A == 1</c>, and a surviving leaf that carries its own <c>IsNot</c> must not be negated
        /// twice. Where <paramref name="parent"/> is a plain operand accumulator its <c>IsNot</c> is
        /// <c>false</c> and the XOR is the identity, so nothing else changes.</para>
        /// <para>⚠ The sibling flag was already known to have this shape — see the comment on the
        /// <c>.Date</c> range branch, which nests rather than merging precisely because this method would
        /// overwrite a nested <c>IsOr</c>. The <c>IsNot</c> half went unnoticed beside it, which is why the
        /// fix is stated here rather than at the call sites.</para>
        /// <para><b>TASK-866 — the <c>IsOr</c> half: the survivor keeps its own separator.</b> On a group
        /// <c>IsOr</c> says how its children combine, and the children are now the survivor's. The collapsed
        /// operator's separator joined two operands, one of which is gone, so it combines nothing; and the parent
        /// brings no separator of its own (it is a fresh operand or a <c>Not</c> wrapper, neither of which sets
        /// one). So the combine of the two is the survivor's flag. This method used to assign the collapsed
        /// operator's: <c>!((x.A == 10 || x.A == 20) &amp;&amp; t)</c> rendered <c>NOT (A = 10 AND A = 20)</c>, and a
        /// Delete removed every row with no exception (measured on SQLite, PostgreSQL, MySQL and SQL Server). The
        /// <c>IsNot</c> XOR is unaffected: both negations still wrap the same set of children.</para>
        /// </remarks>
        private static IEnumerable<Conditions.Condition> ReturnSingleSubCondition(Conditions.Condition? parent, Conditions.Condition surviving)
        {
            if (parent != null)
            {
                // TASK-869: one producer for "every property that says what the predicate is" (Condition.Copy shares it).
                parent.BecomePredicateOf(surviving);
                parent.IsOr = surviving.IsOr;
                parent.IsNot = parent.IsNot ^ surviving.IsNot;
                return new[] { parent };
            }
            return new[] { surviving };
        }

        /// <summary>
        /// Returns the bool value of an expression without allocating a Condition object.
        /// Handles literal constants and parameter-free closure expressions.
        /// Returns null when the expression references a lambda parameter (must go through normal parsing).
        /// </summary>
        private static bool? TryGetLiteralBool(Expression expr)
        {
            if (expr is ConstantExpression ce && ce.Value is bool b) return b;
            if (ContainsParameter(expr)) return null;
            try { return EvaluateExpression(expr) as bool?; }
            catch { return null; }
        }

        /// <summary>
        /// True when <paramref name="expr"/> is a <c>.Date</c> truncation of a DateTime COLUMN
        /// (<c>x.OpenedAt.Date</c>, or <c>x.Nullable.Value.Date</c>), yielding the inner column
        /// expression. False for <c>local.Date</c> — that side has no lambda parameter and is a value.
        /// </summary>
        private static bool TryGetDateTruncatedColumn(Expression expr, out Expression inner)
        {
            inner = null!;
            if (UnwrapConvert(expr) is not MemberExpression m) return false;
            if (m.Member.Name != "Date" || m.Member.DeclaringType != typeof(DateTime)) return false;
            if (m.Expression is not MemberExpression innerMember) return false;
            if (!ContainsParameter(innerMember)) return false;
            inner = innerMember;
            return true;
        }

        /// <summary>
        /// Rewrites <c>x.Col.Date &lt;op&gt; value</c> into a half-open range over the RAW column, e.g.
        /// <c>x.OpenedAt.Date == d</c> becomes <c>OpenedAt &gt;= d AND OpenedAt &lt; d+1day</c>.
        /// Returns null when the shape does not apply (column-vs-column, or neither side a column
        /// <c>.Date</c>), leaving the older <c>DATE(col)</c> rendering in place for those.
        /// <para>
        /// Why this exists (Symbio TASK-355). The previous translation emitted <c>DATE(col) = @p</c> and
        /// bound the right-hand side as a <b>DateTime</b>. On SQLite a DateTime column is stored as the
        /// text <c>yyyy-MM-dd HH:mm:ss.FFFFFFF</c>, so <c>DATE(col)</c> evaluates to the 10-character
        /// <c>yyyy-MM-dd</c> while the parameter serialises to the full <c>yyyy-MM-dd 00:00:00</c> —
        /// <c>'2026-08-07' = '2026-08-07 00:00:00'</c> is <b>false for every row, always</b>. Measured
        /// against the Symbio Testing DB: 0 rows matched where 4 should have. The query runs, returns
        /// 200 and reports zero — the silent-wrong-answer shape, invisible to any test that runs against
        /// an in-memory store because that COMPILES the lambda instead of translating it.
        /// </para>
        /// <para>
        /// The range form fixes three things at once: it is correct (no text-vs-text formatting
        /// mismatch), it is <b>sargable</b> (a function on the column defeats an index), and it is
        /// dialect-agnostic — <c>DATE(x)</c> is not a function in T-SQL at all, so the old form was a
        /// hard syntax error on MSSql, the same works-on-SQLite-only trap as the empty <c>IN ()</c>.
        /// </para>
        /// </summary>
        private static Conditions.Condition? TryBuildDateTruncatedComparison(
            Expression left, Expression right, ExpressionType nodeType, Type? exprType)
        {
            var leftIsCol = TryGetDateTruncatedColumn(left, out var leftInner);
            var rightIsCol = TryGetDateTruncatedColumn(right, out var rightInner);
            if (leftIsCol == rightIsCol) return null;   // both or neither — not this shape

            var columnExpr = leftIsCol ? leftInner : rightInner;
            var valueExpr = leftIsCol ? right : left;
            if (ContainsParameter(valueExpr)) return null;

            // Mirror the operator when the column is on the right (`value == x.Col.Date`).
            var op = nodeType;
            if (!leftIsCol)
            {
                op = op switch
                {
                    ExpressionType.LessThan => ExpressionType.GreaterThan,
                    ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
                    ExpressionType.GreaterThan => ExpressionType.LessThan,
                    ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
                    _ => op,   // Equal / NotEqual are symmetric
                };
            }

            DateTime day;
            try
            {
                if (EvaluateExpression(valueExpr) is not DateTime dt) return null;
                day = dt.Date;
            }
            catch { return null; }
            var next = day.AddDays(1);

            // Resolve the column name through the normal machinery so table-name qualification,
            // nullable unwrapping and [Column] mapping all behave exactly as they do elsewhere.
            var probe = new Conditions.Condition(null, null);
            ParseConditionExpression(columnExpr, probe, exprType);
            if (string.IsNullOrEmpty(probe.Name)) return null;
            var col = probe.Name!;

            Conditions.Condition Leaf(DateTime v, ConditionType t)
                => new Conditions.Condition(col, new object[] { v }, t);

            return op switch
            {
                // date(col) == d  ⟺  d <= col < d+1
                ExpressionType.Equal => new Conditions.Condition(null, null)
                {
                    IsOr = false,
                    SubConditions = new List<Condition>
                    {
                        Leaf(day, ConditionType.GreatherAndEqual),
                        Leaf(next, ConditionType.Less),
                    },
                },
                // date(col) != d  ⟺  col < d OR col >= d+1
                ExpressionType.NotEqual => new Conditions.Condition(null, null)
                {
                    IsOr = true,
                    SubConditions = new List<Condition>
                    {
                        Leaf(day, ConditionType.Less),
                        Leaf(next, ConditionType.GreatherAndEqual),
                    },
                },
                ExpressionType.LessThan => Leaf(day, ConditionType.Less),                    // < d
                ExpressionType.LessThanOrEqual => Leaf(next, ConditionType.Less),            // < d+1
                ExpressionType.GreaterThan => Leaf(next, ConditionType.GreatherAndEqual),    // >= d+1
                ExpressionType.GreaterThanOrEqual => Leaf(day, ConditionType.GreatherAndEqual), // >= d
                _ => null,
            };
        }

        /// <summary>Strips outer Convert / ConvertChecked wrappers (nullable lifting, boxing).</summary>
        private static Expression UnwrapConvert(Expression expr)
            => expr is UnaryExpression u && u.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked
                ? UnwrapConvert(u.Operand)
                : expr;

        private static bool IsArithmeticNode(ExpressionType type)
            => type is ExpressionType.Add or ExpressionType.AddChecked
                or ExpressionType.Subtract or ExpressionType.SubtractChecked
                or ExpressionType.Multiply or ExpressionType.MultiplyChecked
                or ExpressionType.Divide or ExpressionType.Modulo;

        private static bool IsNumericType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return Type.GetTypeCode(type) is TypeCode.Byte or TypeCode.SByte
                or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32
                or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;
        }

        /// <summary>True for a numeric arithmetic BinaryExpression (the column-arithmetic trigger).</summary>
        private static bool IsArithmeticOperand(Expression expr)
            => expr is BinaryExpression b && IsArithmeticNode(b.NodeType) && IsNumericType(b.Type);

        /// <summary>
        /// True when a comparison operand is a value-expression that must be rendered to a raw SQL
        /// fragment: numeric arithmetic, null-coalescing, or a (value-position) ternary. Boolean-typed
        /// ternary / coalesce are desugared to AND/OR by the normalizer and never reach here.
        /// </summary>
        private static bool IsValueExpressionOperand(Expression expr)
            => IsArithmeticOperand(expr)
                || (expr is BinaryExpression b && b.NodeType == ExpressionType.Coalesce)
                || expr is ConditionalExpression
                || IsMathOperand(expr);

        /// <summary>
        /// TASK-857 — a <c>Math</c> / <c>MathF</c> / <c>decimal</c> call or a numeric unary operator over the row. These are
        /// value fragments too: <see cref="RenderValueFragment"/> translates <c>Round</c> (explicit mode), <c>Abs</c> and a
        /// unary minus, and refuses every other one by name. Before, the comparison parser recursed into the call's
        /// arguments and compared the bare column: <c>Math.Round(x.Amount, 1, ToEven) == 10.1m</c> read <c>Amount = 10.1</c>,
        /// and a unary minus produced a condition with no name.
        /// </summary>
        private static bool IsMathOperand(Expression expr)
            => ContainsParameter(expr)
                && (IsMathCall(expr)
                    || (expr is UnaryExpression u
                        && u.NodeType is ExpressionType.Negate or ExpressionType.NegateChecked or ExpressionType.UnaryPlus
                            or ExpressionType.OnesComplement or ExpressionType.Not
                        && IsNumericType(u.Type)));

        private static bool IsMathCall(Expression expr)
            => expr is MethodCallExpression { Method.IsStatic: true } call
                && (call.Method.DeclaringType == typeof(Math) || call.Method.DeclaringType == typeof(MathF)
                    || call.Method.DeclaringType == typeof(decimal));

        private static Type UnwrapNullable(Type type) => Nullable.GetUnderlyingType(type) ?? type;

        /// <summary>
        /// A bool column used as a predicate (TASK-153, SH-M135/SH-M136). <c>col &lt;&gt; 0</c> fails on PostgreSQL
        /// (<c>boolean &lt;&gt; integer</c>) and a bare column fails on SQL Server (a BIT is no predicate). Comparing with
        /// the literal <c>'1'</c> is accepted by all four: PostgreSQL casts it to boolean, the others convert it to
        /// the column's number. Measured live in <c>BoolColumnTestLiveTests</c>.
        /// </summary>
        private static string BoolColumnTest(string column) => $"({column} = '1')";

        private static bool IsNullConstant(Expression expr)
            => expr is ConstantExpression c && c.Value == null;

        private static string? ComparisonSqlOperator(ExpressionType type) => type switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "<>",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            _ => null,
        };

        private static ConditionType FlipComparison(ConditionType type) => type switch
        {
            ConditionType.Less => ConditionType.Greather,
            ConditionType.Greather => ConditionType.Less,
            ConditionType.LessAndEqual => ConditionType.GreatherAndEqual,
            ConditionType.GreatherAndEqual => ConditionType.LessAndEqual,
            _ => type,
        };

        /// <summary>
        /// TASK-858 — what the parse resolved while it rendered one value fragment: each column a member reads, each
        /// constant, and whether an operand can be NULL. A provider re-renders the fragment in its own dialect when the
        /// statement is built (TASK-856, and on every provider since TASK-857), and it reads all three from here.
        /// </summary>
        /// <remarks>
        /// Measured before: the re-render resolved the columns again, after the parse had returned. A view store resolves
        /// its view's properties in a scope that lives only for the parse (<c>ScopeFieldResolver</c>), so every OnTheFly
        /// view filter with arithmetic, <c>COALESCE</c>, <c>CASE</c>, <c>Math.Round</c> or column against column threw
        /// "Cannot translate operand 'v.Amount'" on SQLite, PostgreSQL, MySQL and SQL Server. Before TASK-857 PostgreSQL ran
        /// the same filters. Constants were already evaluated once, because the normalizer folds every parameter-free
        /// subtree first; a subtree it could not fold, which the parser then evaluates itself, is kept here too.
        /// </remarks>
        private sealed class FragmentResolution
        {
            private readonly Dictionary<Expression, string?> _columns = new(ReferenceEqualityComparer.Instance);
            private readonly Dictionary<Expression, object?> _constants = new(ReferenceEqualityComparer.Instance);
            private readonly Dictionary<Expression, bool> _nullable = new(ReferenceEqualityComparer.Instance);
            private readonly object _gate = new();

            /// <summary>The column <paramref name="member"/> reads on the row, or null when it reads none.</summary>
            public string? Column(MemberExpression member, Type? exprType)
                => Resolve(_columns, member, () => TryResolveParameterColumn(member, exprType, out var column) ? column : null);

            /// <summary>The value of the parameter-free <paramref name="expr"/>, by <paramref name="evaluate"/> or as the parser evaluates it.</summary>
            public object? Constant(Expression expr, Func<object?>? evaluate = null)
                => Resolve(_constants, expr, evaluate ?? (() => EvaluateExpression(expr)));

            /// <summary><see cref="OperandCanBeNull"/> of <paramref name="operand"/>.</summary>
            public bool CanBeNull(Expression operand, Type? exprType)
                => Resolve(_nullable, operand, () => OperandCanBeNull(operand, exprType));

            private T Resolve<T>(Dictionary<Expression, T> resolved, Expression key, Func<T> resolve)
            {
                lock (_gate)
                {
                    if (resolved.TryGetValue(key, out var known)) return known;
                }
                var value = resolve();
                lock (_gate)
                {
                    resolved.TryAdd(key, value);
                    return resolved[key];
                }
            }
        }

        /// <summary>
        /// Renders a value-expression subtree — column references, Add/Subtract/Multiply/Divide/Modulo
        /// arithmetic, <c>COALESCE</c>, a value-position ternary (<c>CASE WHEN … END</c>), nullable
        /// <c>.Value</c> unwrap, and constants — into a raw SQL fragment such as <c>(Table.A + Table.B)</c>
        /// or <c>CASE WHEN (Table.Vip = '1') THEN Table.Premium ELSE Table.Score END</c> (a bool column test is <see cref="BoolColumnTest"/>). Because the
        /// WHERE builder binds parameters only later (via the condition strategies), constants inside a
        /// fragment cannot be parameterised and are instead inlined as portable SQL literals
        /// (see <see cref="InlineConstant"/>). Throws <see cref="NotSupportedException"/> for anything it
        /// cannot faithfully translate rather than silently dropping it.
        /// <para>TASK-858: every column and constant is read through <paramref name="resolved"/>, which the parse fills, so a
        /// provider's re-render at statement time reuses what the parse resolved.</para>
        /// </summary>
        private static string RenderValueFragment(Expression expr, Type? exprType, FragmentResolution resolved, SqlValueFragmentDialect? dialect = null)
        {
            dialect ??= SqlValueFragmentDialect.Portable;
            switch (expr)
            {
                case UnaryExpression u when u.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked:
                    if (ContainsParameter(u))
                        // TASK-860: refused here whatever the dialect, so the portable render refuses at parse time too.
                        RequireTranslatableConversion(u.Operand.Type, u.Type);
                    else if (SqlConversion.Classify(u.Operand.Type, u.Type) == SqlConversionKind.Lossy)
                    {
                        // A constant's conversion is C#'s to compute, exactly.
                        var converted = resolved.Constant(u, () => Expression.Lambda(u).Compile().DynamicInvoke());
                        return (converted == null ? null : dialect.Literal(converted)) ?? InlineConstant(converted);
                    }
                    return dialect.Conversion(RenderValueFragment(u.Operand, exprType, resolved, dialect), u.Operand.Type, u.Type);
                case UnaryExpression u when u.NodeType is ExpressionType.Negate or ExpressionType.NegateChecked
                        && ContainsParameter(u):
                    return dialect.Negate(RenderValueFragment(u.Operand, exprType, resolved, dialect), u.Type);
                case UnaryExpression u when u.NodeType == ExpressionType.UnaryPlus && ContainsParameter(u):
                    return RenderValueFragment(u.Operand, exprType, resolved, dialect);
                case UnaryExpression u when ContainsParameter(u) && (u.NodeType != ExpressionType.Not || IsNumericType(u.Type)):
                    // An integer `~` is a Not node in an expression tree, which the predicate parser read as a negation of
                    // the comparison (TASK-857, measured): `~x.Hits == -3` became NOT (Hits = -3).
                    throw new NotSupportedException(
                        $"Cannot translate the {(u.NodeType == ExpressionType.Not ? "bitwise complement (~)" : u.NodeType.ToString())} "
                        + "operator over the row to SQL: no provider computes it as C# does. "
                        + "Compute the value in memory, or compare the column itself.");
                case MethodCallExpression call when IsMathCall(call) && ContainsParameter(call):
                    return RenderMathCall(call, exprType, resolved, dialect);
                case ConditionalExpression cond:
                    return $"CASE WHEN {RenderBoolFragment(cond.Test, exprType, resolved, dialect)} "
                        + $"THEN {RenderValueFragment(cond.IfTrue, exprType, resolved, dialect)} "
                        + $"ELSE {RenderValueFragment(cond.IfFalse, exprType, resolved, dialect)} END";
                case BinaryExpression b when IsArithmeticNode(b.NodeType):
                {
                    var l = RenderValueFragment(b.Left, exprType, resolved, dialect);
                    var r = RenderValueFragment(b.Right, exprType, resolved, dialect);
                    var op = b.NodeType switch
                    {
                        ExpressionType.Add or ExpressionType.AddChecked => "+",
                        ExpressionType.Subtract or ExpressionType.SubtractChecked => "-",
                        ExpressionType.Multiply or ExpressionType.MultiplyChecked => "*",
                        ExpressionType.Divide => "/",
                        ExpressionType.Modulo => "%",
                        _ => throw new NotSupportedException($"Unsupported arithmetic operator {b.NodeType}"),
                    };
                    return dialect.Arithmetic(b.NodeType, op, l, r, b.Type);
                }
                case BinaryExpression b when b.NodeType == ExpressionType.Coalesce:
                    return $"COALESCE({RenderValueFragment(b.Left, exprType, resolved, dialect)}, {RenderValueFragment(b.Right, exprType, resolved, dialect)})";
                case MemberExpression m when resolved.Column(m, exprType) is { } column:
                    return column;
                case MemberExpression valueMember when valueMember.Member.Name == "Value"
                    && valueMember.Member.ReflectedType != null
                    && Nullable.GetUnderlyingType(valueMember.Member.ReflectedType) != null
                    && valueMember.Expression is MemberExpression innerNullable:
                    return RenderValueFragment(innerNullable, exprType, resolved, dialect);
                default:
                    if (!ContainsParameter(expr))
                    {
                        var constant = resolved.Constant(expr);
                        return (constant == null ? null : dialect.Literal(constant)) ?? InlineConstant(constant);
                    }
                    throw new NotSupportedException(
                        $"Cannot translate operand '{expr}' inside a value-expression filter predicate to SQL.");
            }
        }

        /// <summary>
        /// TASK-857 — a <c>Math</c> / <c>MathF</c> / <c>decimal</c> call over the row. <c>Round</c> over a <c>decimal</c> with an
        /// explicit <see cref="MidpointRounding"/> is the provider's computed-set rounding (TASK-826, through the dialect),
        /// <c>Abs</c> is <c>ABS</c> and <c>decimal.Negate</c> a unary minus. Everything else is refused by name: no provider
        /// computes a <c>Floor</c>, <c>Max</c> or <c>Pow</c> exactly as C# does on every type, and a <c>Round</c> without a mode
        /// rounds half to even in C# and half away from zero in SQL.
        /// </summary>
        private static string RenderMathCall(MethodCallExpression call, Type? exprType, FragmentResolution resolved, SqlValueFragmentDialect dialect)
        {
            var name = $"{call.Method.DeclaringType?.Name}.{call.Method.Name}";
            switch (call.Method.Name)
            {
                case nameof(Math.Round) when Birko.Data.Stores.ComputedValue.IsDecimalRound(call.Method):
                {
                    var refusal = Birko.Data.Stores.ComputedValue.ReadRound(call, ContainsParameter, out var digits, out var mode);
                    if (refusal != null)
                        throw new NotSupportedException($"Cannot translate {name} to SQL: {refusal}");
                    return dialect.Round(RenderValueFragment(call.Arguments[0], exprType, resolved, dialect), digits, mode, call.Type);
                }
                case nameof(Math.Round):
                    throw new NotSupportedException(
                        $"Cannot translate {name} over a {call.Method.ReturnType.Name} to SQL: a binary floating-point value rounds "
                        + "differently in SQL than in C#. Round a decimal, with an explicit MidpointRounding.");
                case nameof(Math.Abs) when call.Arguments.Count == 1:
                    return dialect.Abs(RenderValueFragment(call.Arguments[0], exprType, resolved, dialect), call.Type);
                case nameof(decimal.Negate) when call.Arguments.Count == 1 && call.Method.DeclaringType == typeof(decimal):
                    return dialect.Negate(RenderValueFragment(call.Arguments[0], exprType, resolved, dialect), call.Type);
                default:
                    throw new NotSupportedException(
                        $"Cannot translate {name} over the row to SQL: only Math.Round (decimal, explicit MidpointRounding), "
                        + "Math.Abs and a unary minus are translated. Before this was refused the call was dropped and its "
                        + "argument compared instead. Compute the value in memory, or compare the column itself.");
            }
        }

        /// <summary>
        /// Renders a boolean sub-expression — the test of a value-position ternary, i.e. a WHEN clause
        /// of a CASE emitted in a WHERE — into a raw SQL predicate fragment with any constants inlined.
        /// Supports comparisons, IS [NOT] NULL, AND/OR/NOT and bare boolean columns. Throws
        /// <see cref="NotSupportedException"/> for constructs it cannot inline (e.g. string LIKE methods),
        /// rather than silently dropping them.
        /// </summary>
        private static string RenderBoolFragment(Expression expr, Type? exprType, FragmentResolution resolved, SqlValueFragmentDialect? dialect = null)
        {
            dialect ??= SqlValueFragmentDialect.Portable;
            expr = UnwrapConvert(expr);
            switch (expr)
            {
                case UnaryExpression u when u.NodeType == ExpressionType.Not:
                    return $"(NOT {RenderBoolFragment(u.Operand, exprType, resolved, dialect)})";
                case BinaryExpression b when b.NodeType is ExpressionType.AndAlso or ExpressionType.And:
                    return $"({RenderBoolFragment(b.Left, exprType, resolved, dialect)} AND {RenderBoolFragment(b.Right, exprType, resolved, dialect)})";
                case BinaryExpression b when b.NodeType is ExpressionType.OrElse or ExpressionType.Or:
                    return $"({RenderBoolFragment(b.Left, exprType, resolved, dialect)} OR {RenderBoolFragment(b.Right, exprType, resolved, dialect)})";
                case BinaryExpression b when b.NodeType is ExpressionType.Equal or ExpressionType.NotEqual
                        && (IsNullConstant(b.Left) || IsNullConstant(b.Right)):
                {
                    var operand = IsNullConstant(b.Right) ? b.Left : b.Right;
                    var frag = RenderValueFragment(operand, exprType, resolved, dialect);
                    return b.NodeType == ExpressionType.Equal ? $"({frag} IS NULL)" : $"({frag} IS NOT NULL)";
                }
                case BinaryExpression b when ComparisonSqlOperator(b.NodeType) is string op:
                {
                    var left = RenderValueFragment(b.Left, exprType, resolved, dialect);
                    var right = RenderValueFragment(b.Right, exprType, resolved, dialect);
                    var leftNull = ContainsParameter(b.Left) && resolved.CanBeNull(b.Left, exprType);
                    var rightNull = ContainsParameter(b.Right) && resolved.CanBeNull(b.Right, exprType);
                    string Compare(string sqlOperator)
                        => $"({dialect.Compared(left, b.Left.Type)} {sqlOperator} {dialect.Compared(right, b.Right.Type)})";
                    if (!leftNull && !rightNull)
                        return Compare(op);

                    // TASK-859: a WHEN test is two-valued, as in C#: a lifted comparison with a NULL operand is false, but
                    // null == null is true; `!=` is the negation of that. Two-valued, so an enclosing NOT stays exact.
                    var notNull = string.Join(" AND ", new[] { leftNull ? $"{left} IS NOT NULL" : null, rightNull ? $"{right} IS NOT NULL" : null }
                        .Where(test => test != null));
                    string Defined(string sqlOperator) => $"({Compare(sqlOperator)} AND {notNull})";
                    if (b.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual))
                        return Defined(op);
                    var equal = leftNull && rightNull
                        ? $"({Defined("=")} OR ({left} IS NULL AND {right} IS NULL))"
                        : Defined("=");
                    return b.NodeType == ExpressionType.Equal ? equal : $"(NOT {equal})";
                }
                case MemberExpression m when UnwrapNullable(m.Type) == typeof(bool) && resolved.Column(m, exprType) is { } column:
                    return BoolColumnTest(column);
                default:
                    if (!ContainsParameter(expr) && resolved.Constant(expr) is bool constBool)
                        return constBool ? "(1=1)" : "(1=0)";
                    throw new NotSupportedException(
                        $"Cannot translate boolean sub-expression '{expr}' inside a CASE/WHERE predicate to SQL.");
            }
        }

        /// <summary>
        /// Inlines a constant into a portable SQL literal for use inside a raw fragment (CASE/COALESCE/
        /// arithmetic in a WHERE, where parameters cannot be bound): NULL, numeric (invariant), bool → 1/0,
        /// enum → integer, string → single-quoted with <c>'</c> escaped. Throws for types whose literal form
        /// is not portable (DateTime, Guid, byte[]) so they fail loud instead of rendering wrong SQL.
        /// </summary>
        private static string InlineConstant(object? value)
        {
            switch (value)
            {
                case null:
                    return "NULL";
                case bool b:
                    return b ? "1" : "0";
                case string s:
                    // SH-M139 (TASK-153): doubling `'` contains a literal only where backslash is not an escape.
                    // MySQL's default sql_mode honours `\'`, so `\' OR 1=1 -- ` left the literal there. This
                    // renderer has no dialect, and a parameter cannot be bound inside the fragment, so it refuses.
                    if (s.IndexOf('\\') >= 0)
                        throw new NotSupportedException(
                            "Cannot inline a string containing a backslash into a CASE/COALESCE/arithmetic SQL fragment: "
                            + "MySQL reads it as an escape. Compare the column with the value directly instead.");
                    return "'" + SqlLiteral.EscapeLiteral(s) + "'";
                case Enum e:
                    return Convert.ToInt64(e, System.Globalization.CultureInfo.InvariantCulture)
                        .ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            if (IsNumericType(value.GetType()))
                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!;
            throw new NotSupportedException(
                $"Cannot inline a constant of type {value.GetType()} into a CASE/COALESCE/arithmetic SQL fragment.");
        }

        /// <summary>
        /// True when <paramref name="expr"/> is a direct column reference on the lambda parameter — the shape
        /// the ordinary parser path already resolves correctly. Used by the set-<c>Contains</c> arm (TASK-213)
        /// to leave every working operand on its existing path and route only the computed ones through
        /// <see cref="RenderValueFragment"/>.
        /// </summary>
        private static bool IsPlainColumnOperand(Expression expr, Type? exprType)
            => UnwrapConvert(expr) is MemberExpression member
                && TryResolveParameterColumn(member, exprType, out _);

        /// <summary>Resolves a direct parameter member access (or Convert(param).Member) to its column select name.</summary>
        private static bool TryResolveParameterColumn(MemberExpression member, Type? exprType, out string column)
        {
            column = string.Empty;
            if (exprType == null) return false;
            var isParamAccess = member.Expression?.NodeType == ExpressionType.Parameter
                || (member.Expression is UnaryExpression c
                    && c.NodeType == ExpressionType.Convert
                    && c.Operand.NodeType == ExpressionType.Parameter);
            if (!isParamAccess) return false;
            if (member.Member.ReflectedType?.IsAssignableFrom(exprType) != true) return false;
            var name = ResolveColumnName(exprType, member.Member.Name, true);
            if (string.IsNullOrEmpty(name)) return false;
            column = name;
            return true;
        }

        /// <summary>
        /// Builds a <see cref="Condition"/> for a comparison in which at least one side is a value-expression
        /// (arithmetic / COALESCE / CASE). The parameter/column side becomes the condition Name (raw SQL
        /// fragment); a constant side becomes a bound value; when both sides reference the parameter the
        /// right side is emitted verbatim (IsField). Flips the operator when the value is on the left.
        /// </summary>
        private static Condition BuildValueComparison(
            Expression left, Expression right, ConditionType type, bool isNot, bool isOR, Type? exprType, Type comparedType)
        {
            var leftHasParam = ContainsParameter(left);
            var rightHasParam = ContainsParameter(right);
            var resolved = new FragmentResolution();

            if (leftHasParam && !rightHasParam)
            {
                return MarkNullableFragment(
                    WithFragments(MakeValueCondition(RenderValueFragment(left, exprType, resolved), EvaluateExpression(right), type, isNot, isOR),
                        ComparedFragment(left, comparedType, exprType, resolved)),
                    left, exprType, resolved);
            }
            if (!leftHasParam && rightHasParam)
            {
                // Value on the left, column expression on the right — flip the operator to keep the column on the left.
                return MarkNullableFragment(
                    WithFragments(MakeValueCondition(RenderValueFragment(right, exprType, resolved), EvaluateExpression(left), FlipComparison(type), isNot, isOR),
                        ComparedFragment(right, comparedType, exprType, resolved)),
                    right, exprType, resolved);
            }
            // Both sides reference the parameter → column expression compared to column expression.
            var leftFragment = RenderValueFragment(left, exprType, resolved);
            var rightFragment = RenderValueFragment(right, exprType, resolved);
            return WithFragments(new Condition(leftFragment, new object[] { rightFragment }, type, isField: true, isNot: isNot, isOr: isOR),
                ComparedFragment(left, comparedType, exprType, resolved), ComparedFragment(right, comparedType, exprType, resolved));
        }

        /// <summary>
        /// TASK-856 — the re-render of a compared value fragment for a provider dialect: the fragment, converted to the
        /// type the comparison is performed in, as an operand of that comparison. The portable dialect renders exactly
        /// what <see cref="RenderValueFragment"/> wrote into the condition's Name.
        /// </summary>
        private static Func<SqlValueFragmentDialect, string> ComparedFragment(Expression operand, Type comparedType, Type? exprType,
            FragmentResolution resolved)
            => dialect => dialect.Compared(
                dialect.Conversion(RenderValueFragment(operand, exprType, resolved, dialect), operand.Type, comparedType), comparedType);

        private static Condition WithFragments(Condition condition, Func<SqlValueFragmentDialect, string> name,
            Func<SqlValueFragmentDialect, string>? field = null)
        {
            condition.NameFragment = name;
            condition.FieldFragment = field;
            return condition;
        }

        /// <summary>
        /// A comparison whose two operands both read the row (TASK-574). Each side is rendered as a raw fragment and
        /// the right one emitted verbatim (IsField). <c>==</c> and <c>!=</c> between operands that can be null are
        /// rendered null-safe, so they answer what C# answers (<c>null == null</c> is true), as the constant
        /// <c>== null</c> path already does. A <c>.Date</c> on a column is refused: the parser has no dialect, and no
        /// date truncation is portable (<c>DATE()</c> is not T-SQL, <c>CAST(... AS DATE)</c> reads SQLite's stored
        /// text as a number). Refusing beats the wrong answer this used to give.
        /// </summary>
        private static Condition BuildColumnComparison(
            Expression left, Expression right, ExpressionType nodeType, ConditionType type, bool isNot, bool isOR, Type? exprType, Type comparedType)
        {
            if (IsColumnDate(left) || IsColumnDate(right))
                throw new NotSupportedException(
                    $"Cannot translate '{left} {nodeType} {right}' to SQL: comparing the .Date of a column with another "
                    + "column has no portable SQL form. Compare the raw columns, or compare one column's .Date with a value.");

            var resolved = new FragmentResolution();
            var leftFragment = RenderValueFragment(left, exprType, resolved);
            var rightFragment = RenderValueFragment(right, exprType, resolved);
            var compared = WithFragments(new Condition(leftFragment, new object[] { rightFragment }, type, isField: true, isNot: isNot, isOr: isOR),
                ComparedFragment(left, comparedType, exprType, resolved), ComparedFragment(right, comparedType, exprType, resolved));

            var nullable = CanBeNull(left.Type) || CanBeNull(right.Type);
            var nullSafe = nullable && nodeType is (ExpressionType.Equal or ExpressionType.NotEqual);

            // TASK-859: the comparison itself answers false in C# when either side is NULL, so under a negation it gains
            // `AND side IS NOT NULL` (ApplyCSharpNullSemantics). Inside the null-safe group below the NULL cases are the
            // group's other terms, so the `A <> B` term must answer false on NULL too, and its IsNot reads that as "the
            // equality it negates answers true".
            var nullableSides = new[] { (Operand: left, Sql: leftFragment), (Operand: right, Sql: rightFragment) }
                .Where(side => OperandCanBeNull(side.Operand, exprType))
                .Select(side => FragmentOperand(side.Sql, side.Operand, exprType, resolved))
                .ToArray();
            if (nullableSides.Length > 0 && IsComparison(type))
            {
                compared.NullableOperands = nullableSides;
                compared.NullOperandMatches = nullSafe && nodeType == ExpressionType.NotEqual;
            }

            if (!nullSafe)
                return compared;

            // TASK-857: each IS [NOT] NULL leg keeps its provider rendering, as the comparison does; the portable form of a
            // Round is no server's SQL.
            Condition Null(Expression operand, string fragment, bool not)
                => WithFragments(new Condition(fragment, null, ConditionType.IsNull, isNot: not),
                    dialect => RenderValueFragment(operand, exprType, resolved, dialect));
            Condition All(params Condition[] subs) => new Condition(null, null) { IsOr = false, SubConditions = subs.ToList() };

            if (nodeType == ExpressionType.Equal)
            {
                // A = B OR (A IS NULL AND B IS NULL)
                return new Condition(null, null)
                {
                    IsOr = true,
                    SubConditions = new List<Condition> { compared, All(Null(left, leftFragment, false), Null(right, rightFragment, false)) },
                };
            }
            // A <> B OR (A IS NULL AND B IS NOT NULL) OR (A IS NOT NULL AND B IS NULL)
            return new Condition(null, null)
            {
                IsOr = true,
                SubConditions = new List<Condition>
                {
                    compared,
                    All(Null(left, leftFragment, false), Null(right, rightFragment, true)),
                    All(Null(left, leftFragment, true), Null(right, rightFragment, false)),
                },
            };
        }

        private static bool CanBeNull(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) != null;

        /// <summary>True for <c>x.Col.Date</c> (through any Convert / nullable <c>.Value</c>) on a row column.</summary>
        private static bool IsColumnDate(Expression expr)
            => UnwrapConvert(expr) is MemberExpression m
                && m.Member.Name == "Date"
                && m.Member.DeclaringType == typeof(DateTime)
                && m.Expression != null
                && ContainsParameter(m.Expression);

        private static Condition MakeValueCondition(string name, object? value, ConditionType type, bool isNot, bool isOR)
        {
            // A null-valued equality must become IS NULL (see the literal `== null` path); an ordering against null is
            // the constant C# answers (TASK-859): false, and true under a negation.
            var condition = new Condition(name, new[] { value! }, type, false, isNot, isOR);
            if (value == null)
                ComparedWithNull(condition);
            return condition;
        }

        /// <summary>
        /// TASK-859 — a comparison whose value is <c>null</c>. An equality is the operand's <c>IS [NOT] NULL</c>; a lifted
        /// ordering (<c>&lt;</c>, <c>&gt;</c>, <c>&lt;=</c>, <c>&gt;=</c>) is <c>false</c> in C# whatever the row, so it becomes
        /// the empty <c>IN</c>: <c>1 = 0</c>, or under <see cref="Condition.IsNot"/> the always-true term TASK-137 reduces
        /// away. However deep it sits, <see cref="Connectors.AbstractConnectorBase.ConstantTruth"/> reduces the enclosing
        /// groups with it (TASK-861), so a filter that therefore means every row — <c>!(x.Score &gt; nul)</c> or
        /// <c>!(x.Score &gt; nul &amp;&amp; x.Amount &gt; 3)</c> — is refused by a destructive statement and reads every row.
        /// An ordering used to bind the null (UNKNOWN, so no row, but every row under a <c>NOT</c>) or, from a closure,
        /// to become <c>IS NULL</c>.
        /// </summary>
        internal static void ComparedWithNull(Condition condition)
        {
            condition.NullableOperands = null;
            if (condition.Type is ConditionType.Less or ConditionType.Greather or ConditionType.LessAndEqual or ConditionType.GreatherAndEqual)
            {
                condition.Type = ConditionType.In;
                condition.Values = Array.Empty<object>();
                return;
            }
            condition.Type = ConditionType.IsNull;
            condition.Values = null;
        }

        /// <summary>
        /// TASK-859 — true when <paramref name="operand"/> can read NULL: its CLR type can hold null and, for a plain column,
        /// the column is not declared NOT NULL or primary. A non-nullable <c>int</c> column keeps the SQL it always had.
        /// </summary>
        private static bool OperandCanBeNull(Expression operand, Type? exprType)
        {
            var inner = UnwrapConvert(operand);
            if (!CanBeNull(inner.Type)) return false;
            if (exprType != null && ComparedField(inner, exprType) is { } field && !ColumnMayHoldNull(field)) return false;
            return true;
        }

        /// <summary>TASK-859 — false for a column declared NOT NULL or primary; its CLR type is the caller's question.</summary>
        internal static bool ColumnMayHoldNull(Fields.AbstractField field) => !field.IsNotNull && !field.IsPrimary;

        /// <summary>
        /// TASK-859 — records the plain column of an <c>==</c> / <c>!=</c> / ordering against a value as a nullable operand,
        /// for <see cref="ApplyCSharpNullSemantics"/>.
        /// </summary>
        private static void MarkNullableComparison(Condition condition, Expression column, Type? exprType)
        {
            if (condition.IsField || string.IsNullOrEmpty(condition.Name) || !IsComparison(condition.Type)
                || !OperandCanBeNull(column, exprType))
                return;
            condition.NullableOperands = new[] { new NullableOperand(condition.Name) };
            condition.NullOperandMatches = false;
        }

        /// <summary>TASK-859 — the same for a value fragment (<c>x.Optional + 1m != 8m</c>), which is NULL when its CLR type allows.</summary>
        private static Condition MarkNullableFragment(Condition condition, Expression operand, Type? exprType, FragmentResolution resolved)
        {
            if (!IsComparison(condition.Type) || !CanBeNull(operand.Type) || string.IsNullOrEmpty(condition.Name))
                return condition;
            condition.NullableOperands = new[] { FragmentOperand(condition.Name, operand, exprType, resolved) };
            condition.NullOperandMatches = false;
            return condition;
        }

        /// <summary>
        /// TASK-859 — a set <c>Contains</c>: a <c>null</c> in the list is taken out of the <c>IN</c> (bound, it made
        /// <c>NOT IN</c> match no row) and recorded as "a NULL operand matches". A list that held only <c>null</c> is the
        /// operand's <c>IS [NOT] NULL</c>; over a column that cannot be NULL, the null simply goes.
        /// </summary>
        private static void MarkNullableIn(Condition condition, Expression operand, Type? exprType, FragmentResolution resolved)
        {
            var nullable = string.IsNullOrEmpty(condition.Name) || !OperandCanBeNull(operand, exprType)
                || (operand.Type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(operand.Type))
                ? null
                : IsPlainColumnOperand(operand, exprType)
                    ? new NullableOperand(condition.Name!)
                    : FragmentOperand(condition.Name!, operand, exprType, resolved);
            MarkNullableIn(condition, nullable);
        }

        /// <summary>
        /// The expression-free half of <see cref="MarkNullableIn(Condition, Expression, Type, FragmentResolution)"/>, shared with
        /// <see cref="Conditions.RuleConditionConverter"/> (TASK-864). <paramref name="nullable"/> is null for an operand that
        /// cannot be NULL, whose list merely loses its nulls.
        /// </summary>
        internal static void MarkNullableIn(Condition condition, NullableOperand? nullable)
        {
            var values = condition.Values?.Cast<object?>().ToList();
            var hadNull = values?.Any(v => v == null) == true;
            if (hadNull)
                condition.Values = values!.Where(v => v != null).ToArray();
            if (nullable == null)
                return;
            if (hadNull && values!.All(v => v == null))
            {
                condition.Type = ConditionType.IsNull;
                condition.Values = null;
                condition.NameFragment = nullable.Fragment;
                return;
            }
            condition.NullableOperands = new[] { nullable };
            condition.NullOperandMatches = hadNull;
        }

        private static NullableOperand FragmentOperand(string portable, Expression operand, Type? exprType, FragmentResolution resolved)
            => new(portable, dialect => RenderValueFragment(operand, exprType, resolved, dialect));

        private static bool IsComparison(ConditionType type)
            => type is ConditionType.Equal or ConditionType.Less or ConditionType.Greather
                or ConditionType.LessAndEqual or ConditionType.GreatherAndEqual;

        /// <summary>
        /// TASK-859 — makes the parsed tree answer what the C# expression answers on a NULL operand. SQL's comparison is
        /// UNKNOWN there, which a WHERE treats as false at even depth of <c>NOT</c> and as true at odd depth (Kleene logic
        /// collapses UNKNOWN that way through AND and OR). So a comparison needs a leg only where C# answers the other way:
        /// <list type="bullet">
        /// <item>outside a negation, a comparison C# answers <b>true</b> on NULL (<c>!=</c>, <c>NOT IN</c> without a null,
        /// <c>IN</c> with one) gains <c>OR operand IS NULL</c>;</item>
        /// <item>inside one, a comparison C# answers <b>false</b> on NULL (<c>==</c>, an ordering, <c>IN</c> without a null)
        /// gains <c>AND operand IS NOT NULL</c>.</item>
        /// </list>
        /// Every other comparison, and every comparison over an operand that cannot be NULL, keeps its SQL. Neither leg can
        /// widen a filter to every row: each is a column test, never a constant, so TASK-137's whole-table guard still sees
        /// exactly what it saw. An <c>IN</c> that gains an <c>OR</c> leg sits in an OR group, which TASK-833's planner never
        /// splits (each chunk would match the NULL rows again).
        /// </summary>
        internal static IEnumerable<Condition> ApplyCSharpNullSemantics(IEnumerable<Condition> roots)
        {
            var terms = roots as IList<Condition> ?? roots.ToList();
            List<Condition>? result = null;
            for (var i = 0; i < terms.Count; i++)
            {
                var term = terms[i];
                var rewritten = WithNullLegs(term, negated: false);
                if (ReferenceEquals(rewritten, term)) continue;
                // A root after the first joins with its own IsOr; the new group's IsOr joins its children.
                if (i > 0 && rewritten.IsOr != term.IsOr)
                    rewritten = new Condition(null, null) { IsOr = term.IsOr, SubConditions = new List<Condition> { rewritten } };
                result ??= terms.ToList();
                result[i] = rewritten;
            }
            return result ?? terms;
        }

        private static Condition WithNullLegs(Condition condition, bool negated)
        {
            if (condition.SubConditions?.Any() == true)
            {
                var inner = negated ^ condition.IsNot;
                var subs = condition.SubConditions.Select(sub => WithNullLegs(sub, inner)).ToList();
                // `!(x.Score > nullValue)`: a negated group around the constant empty IN is the constant's complement. Folded
                // into the leaf, because TASK-137's guard recognises an always-true leaf but not `NOT (1 = 0)`.
                if (condition.IsNot && subs.Count == 1 && subs[0].SubConditions?.Any() != true
                    && subs[0].Type == ConditionType.In && subs[0].Values?.Cast<object?>().Any() != true)
                {
                    var folded = subs[0];
                    folded.IsNot = !folded.IsNot;
                    folded.IsOr = condition.IsOr;
                    return folded;
                }
                condition.SubConditions = subs;
                return condition;
            }

            var operands = condition.NullableOperands;
            if (operands == null || operands.Count == 0) return condition;
            // An empty IN is a constant (`1 = 0`, or the always-true NOT IN), whatever the operand holds.
            if (condition.Type == ConditionType.In && condition.Values?.Cast<object?>().Any() != true) return condition;

            var answersTrueOnNull = condition.IsNot ^ condition.NullOperandMatches;
            if (!negated && answersTrueOnNull)
                return new Condition(null, null)
                {
                    IsOr = true,
                    SubConditions = new[] { condition }.Concat(operands.Select(o => o.IsNull(isNot: false))).ToList(),
                };
            if (negated && !answersTrueOnNull)
                return new Condition(null, null)
                {
                    IsOr = false,
                    SubConditions = new[] { condition }.Concat(operands.Select(o => o.IsNull(isNot: true))).ToList(),
                };
            return condition;
        }

        /// <summary>True when one operand of an equality is the literal <c>null</c>; yields the other operand.</summary>
        private static bool TryGetNullComparisonOperand(BinaryExpression binary, out Expression? nonNullOperand)
        {
            nonNullOperand = null;
            if (IsNullConstant(binary.Right)) { nonNullOperand = binary.Left; return true; }
            if (IsNullConstant(binary.Left)) { nonNullOperand = binary.Right; return true; }
            return false;
        }

        private static object? EvaluateExpression(Expression expr)
        {
            if (expr is ConstantExpression c)
                return c.Value;

            if (expr is MemberExpression m)
            {
                object? container = null;
                if (m.Expression != null)
                {
                    container = EvaluateExpression(m.Expression);
                }

                try
                {
                    if (m.Member is FieldInfo fi)
                        return fi.GetValue(container);
                    if (m.Member is PropertyInfo pi)
                        return pi.GetValue(container);
                }
                catch (TargetException)
                {
                    // Non-static member on null container — fall through to lambda compilation
                }
            }

            // Method calls on evaluated objects (e.g., value.ToLowerInvariant(), list.Contains(x))
            //
            // SH-H024 / TASK-308 — ⚠ DEFENSIVE, NOT WITNESSED, and saying so is the point.
            //
            // Gated on ContainsParameter because a lambda parameter evaluates to null here and
            // Method.Invoke would then succeed with the WRONG arguments rather than fail. That is how
            // SH-H024 stored `"-"` for `string.Concat(r.Name, "-", r.Name)`.
            //
            // Measured: removing this gate reds **0 of 1,236** tests across Birko.Data.SQL.Tests,
            // Birko.Data.SQL.SqLite.Tests and Birko.Data.ElasticSearch.Tests, because ParseExpression's
            // method-call branch now refuses a parameter-bound call before it ever gets here — that
            // refusal is the load-bearing half and it has its own red-verified test.
            //
            // Kept anyway, and NOT because a test demands it: without it this method holds two
            // contradictory contracts, since the compiled-lambda fallback below explicitly refuses a
            // parameter-bound tree while this arm fabricated a value from one. There are 19 call sites,
            // and the next one to reach a parameter-bound method call would inherit the defect rather
            // than the guard. Do not delete it as dead weight — that inconsistency IS SH-H024's root
            // cause, and the call-site refusal only closes the door that happens to exist today.
            if (expr is MethodCallExpression mce && !ContainsParameter(mce))
            {
                object? instance = mce.Object != null ? EvaluateExpression(mce.Object) : null;
                var args = new object?[mce.Arguments.Count];
                for (int i = 0; i < mce.Arguments.Count; i++)
                    args[i] = EvaluateExpression(mce.Arguments[i]);
                return mce.Method.Invoke(instance, args);
            }

            // Unary conversions (e.g., (object)value)
            if (expr is UnaryExpression ue && ue.NodeType == ExpressionType.Convert)
            {
                return EvaluateExpression(ue.Operand);
            }

            if (expr is NewArrayExpression na && na.NodeType == ExpressionType.NewArrayInit)
            {
                var elementType = na.Type.GetElementType();
                if (elementType == null)
                    return null;

                var list = Array.CreateInstance(elementType, na.Expressions.Count);
                for (int i = 0; i < na.Expressions.Count; i++)
                {
                    list.SetValue(EvaluateExpression(na.Expressions[i]), i);
                }
                return list;
            }

            // Fallback: compile as parameterless lambda — but only if the expression
            // does not reference any unbound ParameterExpressions (lambda parameters like 'u').
            if (!ContainsParameter(expr))
            {
                var func = _expressionCache.GetOrAdd(expr, static e =>
                {
                    // Box value types (Guid, int, bool, enum, DateTime) so the compiled
                    // delegate is always Func<object>, not Func<T>.
                    var body = e.Type.IsValueType
                        ? Expression.Convert(e, typeof(object))
                        : e;
                    var lambda = Expression.Lambda<Func<object>>(body);
                    return lambda.Compile();
                });
                return func();
            }

            return null;
        }

        /// <summary>
        /// Checks whether the expression tree contains any ParameterExpression (lambda parameters).
        /// Results are memoized by expression reference so repeated calls on the same node are O(1).
        /// </summary>
        private static bool ContainsParameter(Expression expr)
        {
            return _containsParamCache
                .GetOrAdd(expr, static e => new System.Runtime.CompilerServices.StrongBox<bool>(ContainsParameterCore(e)))
                .Value;
        }

        private static bool ContainsParameterCore(Expression expr)
        {
            if (expr is LambdaExpression lambda)
                return lambda.Parameters.Count > 0;
            if (expr is ParameterExpression)
                return true;
            if (expr is MemberExpression me)
                return me.Expression != null && ContainsParameter(me.Expression);
            if (expr is MethodCallExpression mc)
            {
                if (mc.Object != null && ContainsParameter(mc.Object))
                    return true;
                return mc.Arguments.Any(ContainsParameter);
            }
            if (expr is UnaryExpression ue)
                return ContainsParameter(ue.Operand);
            if (expr is BinaryExpression be)
                return ContainsParameter(be.Left) || ContainsParameter(be.Right);
            if (expr is ConditionalExpression ce)
                return ContainsParameter(ce.Test) || ContainsParameter(ce.IfTrue) || ContainsParameter(ce.IfFalse);
            // SH-M150 (TASK-153): every other node kind (NewArray, New, Invocation, TypeIs, ListInit, Index, ...)
            // used to answer false, so a parameter-bearing one was evaluated as a constant.
            if (expr == null || expr is ConstantExpression)
                return false;
            var finder = new ParameterFinder();
            finder.Visit(expr);
            return finder.Found;
        }

        private sealed class ParameterFinder : ExpressionVisitor
        {
            public bool Found { get; private set; }

            public override Expression? Visit(Expression? node) => Found ? node : base.Visit(node);

            protected override Expression VisitParameter(ParameterExpression node)
            {
                Found = true;
                return node;
            }
        }

        private static IEnumerable<object>? InvokeExpression(Expression expr)
        {
            object? value = EvaluateExpression(expr);
            if (value == null) return null;

            if (value is string)
                return new object[] { value };
            if (value is IEnumerable enumerable)
                return enumerable.Cast<object>().Where(x => x != null);

            return new object[] { value };
        }
    }
}
