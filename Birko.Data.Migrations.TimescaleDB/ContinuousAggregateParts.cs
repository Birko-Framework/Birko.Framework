using System;
using Birko.Data.SQL;

namespace Birko.Data.Migrations.TimescaleDB
{
    /// <summary>
    /// One projected aggregate in a continuous aggregate's SELECT list — <c>fn(column) AS alias</c>.
    /// </summary>
    /// <remarks>
    /// <b>TASK-260 replaced a raw <c>selectClause</c> string with this.</b> That parameter was SQL in
    /// statement position, so no escaping could contain it: a string documented as "SQL" has no containment
    /// story, and the only fix is to stop taking SQL.
    /// <para>
    /// <b>The function is a validated bare IDENTIFIER, not a closed enum — and that is a deliberate
    /// amendment to this task's own acceptance criterion.</b> The criterion asked for a closed set on the
    /// reasoning that "a passthrough is the same hole with more ceremony". Measured on TimescaleDB 2.29.2,
    /// a continuous aggregate accepts essentially <i>any</i> aggregate — <c>array_agg</c>,
    /// <c>string_agg</c>, <c>bool_and</c>, even the ordered-set <c>percentile_cont</c> — plus user-defined
    /// ones. A closed enum would therefore impose a brand-new restriction and refuse aggregates that work
    /// today, which is the wrong kind of guard.
    /// </para>
    /// <para>
    /// <b>This is not a passthrough, and the difference is measurable.</b> A passthrough accepts arbitrary
    /// text; this accepts a single bare identifier and nothing else, through
    /// <see cref="Birko.Data.SQL.DataBase.ValidateColumnIdentifier"/> — the same producer TASK-255 introduced
    /// for the bucketing column, so this file has ONE rule for "a name interpolated bare into a statement".
    /// A payload fails the guard; a name that merely does not exist can only <i>fail</i> the statement, at
    /// DDL time, with <c>42883 function nosuchagg(double precision) does not exist</c> and a HINT naming the
    /// argument types. Nothing swallows it: <c>42883</c> is not <c>42P01</c>, so
    /// <c>IsMissingTableException</c> does not classify it; <c>ExecuteScript</c> has no <c>try</c>/<c>catch</c>;
    /// and TASK-254's schema-ensure degrade is on the store path, not this migration path. That is exactly
    /// the property the validator already claims — <i>a bare identifier that names nothing is at worst a
    /// database error, which is a wrong answer that reports itself.</i>
    /// </para>
    /// </remarks>
    public sealed class ContinuousAggregateProjection
    {
        private ContinuousAggregateProjection(string function, string? column, string? secondColumn, string alias)
        {
            Function = function;
            Column = column;
            SecondColumn = secondColumn;
            Alias = alias;
        }

        /// <summary>The aggregate's name. Validated as a bare identifier when rendered.</summary>
        public string Function { get; }

        /// <summary>
        /// The aggregated column, or <see langword="null"/> for the no-argument form, which renders
        /// <c>*</c>.
        /// </summary>
        public string? Column { get; }

        /// <summary>
        /// The second argument of a two-argument aggregate — TimescaleDB's <c>first(value, time)</c> and
        /// <c>last(value, time)</c>. <see langword="null"/> for the ordinary unary form.
        /// </summary>
        /// <remarks>
        /// Measured rather than assumed: <c>first(Value)</c> does <b>not</b> exist on 2.29.2, so these two
        /// aggregates are strictly binary and a unary-only model could not express them at all.
        /// </remarks>
        public string? SecondColumn { get; }

        /// <summary>The output column name. Validated as a bare identifier when rendered.</summary>
        public string Alias { get; }

        /// <summary><c>fn(column) AS alias</c>.</summary>
        /// <remarks>
        /// <paramref name="column"/> is rejected when null. <c>null</c> is the <c>*</c> sentinel inside this
        /// type and <see cref="Render"/> takes that branch <b>before</b> validating — so a null arriving here
        /// from a config object would silently render <c>count(*)</c> and count every row instead of the
        /// intended column's non-null values. That is a wrong answer which does <i>not</i> report itself,
        /// unlike an empty string, which the identifier guard refuses. Keeping <c>*</c> reachable only through
        /// <see cref="OfAll"/> is the stated design; this is what enforces it. Found by code-review at
        /// TASK-260's close gate.
        /// </remarks>
        public static ContinuousAggregateProjection Of(string function, string column, string alias)
            => new(function,
                   column ?? throw new ArgumentNullException(nameof(column),
                       "Use OfAll(function, alias) for the count(*) form; a null column here would silently "
                       + "aggregate every row instead of the column you meant."),
                   null, alias);

        /// <summary>
        /// <c>fn(*) AS alias</c> — the no-argument form, for <c>count(*)</c>.
        /// </summary>
        /// <remarks>
        /// The <c>*</c> is emitted by this framework and is never caller text, so it is contained by
        /// construction. No whitelist restricts which function may use it: measured, <c>count(*)</c> works
        /// while <c>sum(*)</c> and <c>avg(*)</c> raise <i>function sum() does not exist</i> — nonsense
        /// self-reports, exactly as an unknown name does.
        /// </remarks>
        public static ContinuousAggregateProjection OfAll(string function, string alias)
            => new(function, null, null, alias);

        /// <summary><c>fn(column, secondColumn) AS alias</c> — for <c>first</c> / <c>last</c>.</summary>
        /// <remarks>
        /// Both columns are rejected when null, for the reason on <see cref="Of"/>. A null
        /// <paramref name="secondColumn"/> degrades <c>first(v, ts)</c> to the unary <c>first(v)</c>, which
        /// does not exist (measured, <c>42883</c>) — loud rather than silent, but still not what was asked.
        /// </remarks>
        public static ContinuousAggregateProjection OfPair(string function, string column, string secondColumn, string alias)
            => new(function,
                   column ?? throw new ArgumentNullException(nameof(column)),
                   secondColumn ?? throw new ArgumentNullException(nameof(secondColumn)),
                   alias);

        /// <summary>Renders the SELECT-list entry, validating every identifier it emits.</summary>
        internal string Render()
        {
            var fn = DataBase.ValidateColumnIdentifier(Function, nameof(Function));
            var alias = DataBase.ValidateColumnIdentifier(Alias, nameof(Alias));

            var args = Column == null
                ? "*"
                : DataBase.ValidateColumnIdentifier(Column, nameof(Column))
                  + (SecondColumn == null
                      ? string.Empty
                      : ", " + DataBase.ValidateColumnIdentifier(SecondColumn, nameof(SecondColumn)));

            return $"{fn}({args}) AS {alias}";
        }
    }

    /// <summary>
    /// One extra GROUP BY term in a continuous aggregate, beyond the time bucket — either a bare column or a
    /// function applied to one.
    /// </summary>
    /// <remarks>
    /// <b>Structured like the projection rather than columns-only, and that is a measured decision.</b> An
    /// expression grouping is legal in a continuous aggregate — measured on 2.29.2,
    /// <c>GROUP BY bucket, date_trunc('day', Ts)</c> creates successfully — so restricting groupings to bare
    /// columns would have given up a real capability. With the projection's function set open, refusing it
    /// here would also have been arbitrary rather than principled.
    /// <para>
    /// The optional literal argument (<c>'day'</c>) is contained by
    /// <see cref="Birko.Data.SQL.SqlLiteral.EscapeLiteral"/>, which this file already uses for the time
    /// bucket and the policy intervals; the function and column are validated identifiers as above.
    /// </para>
    /// <para>
    /// <b>Not expressible, deliberately:</b> ordered-set aggregates such as
    /// <c>percentile_cont(0.5) WITHIN GROUP (ORDER BY x)</c>. They are legal in a continuous aggregate
    /// (measured) but their syntax is not function-plus-arguments, so they want their own structured shape
    /// if a caller ever needs one — never a raw string, which is the hole TASK-260 closed.
    /// </para>
    /// </remarks>
    public sealed class ContinuousAggregateGrouping
    {
        private ContinuousAggregateGrouping(string? function, string? literalArgument, string column, string? alias)
        {
            Function = function;
            LiteralArgument = literalArgument;
            Column = column;
            Alias = alias;
        }

        /// <summary>The wrapping function, or <see langword="null"/> for a bare column.</summary>
        public string? Function { get; }

        /// <summary>
        /// A literal first argument, such as <c>day</c> in <c>date_trunc('day', Ts)</c>. Escaped, not
        /// validated as an identifier — it is a value.
        /// </summary>
        public string? LiteralArgument { get; }

        /// <summary>The grouped column.</summary>
        public string Column { get; }

        /// <summary>
        /// The output column name for an expression grouping, or <see langword="null"/> to let PostgreSQL
        /// name it.
        /// </summary>
        /// <remarks>
        /// <b>Without this, two expression groupings collide.</b> A grouping term is spliced into the SELECT
        /// list as well as the GROUP BY, and an unaliased <c>date_trunc(...)</c> takes the function's name as
        /// its output column — so two such groupings emit two columns of the same name and the whole
        /// statement fails with <c>42701 column "date_trunc" specified more than once</c> (measured on
        /// 2.29.2). The raw group-by string this type replaced could alias the expression; without this the
        /// structured surface could not, which would have been a real capability regression hiding inside a
        /// redesign that claimed to preserve the capability. Found by code-review at TASK-260's close gate.
        /// </remarks>
        public string? Alias { get; }

        /// <summary>A bare column grouping — <c>GROUP BY …, DeviceId</c>.</summary>
        public static ContinuousAggregateGrouping Of(string column) => new(null, null, column, null);

        /// <summary><c>fn('literal', column)</c> — e.g. <c>date_trunc('day', Ts)</c>.</summary>
        public static ContinuousAggregateGrouping Expression(string function, string literalArgument, string column,
            string? alias = null)
            => new(function, literalArgument, column, alias);

        /// <summary>
        /// Renders the GROUP BY term — the expression only, never the alias. PostgreSQL groups by the
        /// expression; an alias here would be a syntax error.
        /// </summary>
        internal string Render()
        {
            var column = DataBase.ValidateColumnIdentifier(Column, nameof(Column));
            if (Function == null)
            {
                return column;
            }

            var fn = DataBase.ValidateColumnIdentifier(Function, nameof(Function));
            return LiteralArgument == null
                ? $"{fn}({column})"
                : $"{fn}('{SqlLiteral.EscapeLiteral(LiteralArgument)}', {column})";
        }

        /// <summary>
        /// Renders the SELECT-list form — the same expression, plus <c>AS alias</c> when one is supplied.
        /// </summary>
        /// <remarks>
        /// The two forms deliberately differ, which is why CR-H071's guard is expressed on the <i>emptiness
        /// of the grouping set</i> rather than on the two strings being identical: the dangling comma is what
        /// that finding is about, and it is decided once for both.
        /// </remarks>
        internal string RenderSelect()
        {
            var expression = Render();
            return Alias == null
                ? expression
                : $"{expression} AS {DataBase.ValidateColumnIdentifier(Alias, nameof(Alias))}";
        }
    }
}
