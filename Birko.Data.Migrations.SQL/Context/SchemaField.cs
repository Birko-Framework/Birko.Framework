using System.Data;
using System.Data.Common;
using Birko.Data.Patterns.Schema;
using Birko.Data.SQL.Fields;

namespace Birko.Data.Migrations.SQL.Context
{
    /// <summary>
    /// A <see cref="FieldDescriptor"/> adapted to the SQL layer's field model, so a migration's declared
    /// column metadata reaches the connector's <c>FieldDefinition</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>TASK-264 — use <see cref="For"/>, never <c>new SchemaField(...)</c>.</b> A connector reads a
    /// column's size off the field's <i>runtime type</i>, not off a property:
    /// <c>MSSqlConnector.ConvertType</c> tests <c>field is CharField</c> before it will emit
    /// <c>NVARCHAR(n)</c>, and <c>field is DecimalField &amp;&amp; Precision != null &amp;&amp; Scale != null</c>
    /// before it will emit <c>DECIMAL(p,s)</c>. This class derives straight from
    /// <see cref="AbstractField"/> and satisfies neither test, so for the whole life of the migrations
    /// project a declared <c>maxLength</c> produced the unbounded type and a declared
    /// <c>precision</c>/<c>scale</c> produced a bare <c>DECIMAL</c> — which is <c>DECIMAL(18,0)</c> on SQL
    /// Server, i.e. <b>money truncated to whole units, silently</b>.
    /// </para>
    /// <para>
    /// The factory is the one producer, mirroring <c>AbstractField.CreateAbstractField</c>'s dispatch —
    /// including its <c>MaxLength</c>-then-<c>Precision</c> fallback for strings, which exists there for
    /// backwards compatibility and is matched here so the two paths cannot disagree about what a length is.
    /// Fourth instance of § TASK-245's <i>"when you find the same statement written three times, look for
    /// the field that gets lost on the way in"</i>, after <c>SqlIndexManager</c> (TASK-245),
    /// <c>SqlIndexBuilder.Build()</c> (TASK-246) and the six index builders (TASK-274).
    /// </para>
    /// <para>
    /// ⚠ <b><see cref="AbstractField.IsIndexed"/> is deliberately never set, and it cannot be.</b>
    /// <c>SqlCollectionBuilder</c> and <c>SqlIndexBuilder</c> are separate builders with separate
    /// <c>Build()</c> calls and no shared state — often in different migrations — so at <c>CREATE TABLE</c>
    /// time nothing knows an index will later target a column. <c>DataBase.LoadIndexes</c> can set the flag
    /// only because it sees a whole entity's attributes at once, which has no analogue here. Honouring
    /// <c>MaxLength</c> is what resolves the case that matters: a declared length yields an indexable
    /// column, and a column declared with no length gets the unbounded type and a <b>loud</b> failure if an
    /// index is then declared over it (Msg 1919 on SQL Server, ERROR 1170 on MySQL) — <c>CreateIndexes</c>
    /// swallows only "already exists", so an explicit migration call still throws, per TASK-204.
    /// </para>
    /// </remarks>
    internal class SchemaField : AbstractField
    {
        public SchemaField(FieldDescriptor descriptor)
            : base(null!, ColumnNameOf(descriptor), MapFieldType(descriptor.Type), descriptor.IsPrimary, descriptor.IsRequired, descriptor.IsUnique, descriptor.IsAutoIncrement)
        {
        }

        /// <summary>
        /// Builds the field subclass whose runtime type carries the metadata the descriptor declared.
        /// Every construction site goes through here (TASK-264), so a new one is correct without being told.
        /// </summary>
        public static AbstractField For(FieldDescriptor descriptor)
        {
            if (descriptor.Type == FieldType.String || descriptor.Type == FieldType.Json)
            {
                // MaxLength first, Precision as the fallback -- exactly CreateAbstractField's order.
                var length = (descriptor.MaxLength != null && descriptor.MaxLength > 0)
                    ? descriptor.MaxLength
                    : descriptor.Precision;
                if (length != null && length > 0)
                {
                    return new SchemaCharField(descriptor, length);
                }
            }
            else if (descriptor.Type == FieldType.Decimal
                     && descriptor.Precision != null && descriptor.Scale != null)
            {
                return new SchemaDecimalField(descriptor);
            }

            return new SchemaField(descriptor);
        }

        /// <summary>
        /// The emitted column name. <see cref="FieldDescriptor.ColumnName"/> was silently ignored before
        /// TASK-264, so a migration that named a column got its logical name instead.
        /// </summary>
        internal static string ColumnNameOf(FieldDescriptor descriptor)
            => string.IsNullOrWhiteSpace(descriptor.ColumnName) ? descriptor.Name : descriptor.ColumnName!;

        public override void Read(object value, DbDataReader reader, int index)
        {
            // Schema-only field, not used for data operations
        }

        internal static DbType MapFieldType(FieldType type)
        {
            return type switch
            {
                FieldType.String => DbType.String,
                FieldType.Integer => DbType.Int32,
                FieldType.Long => DbType.Int64,
                FieldType.Decimal => DbType.Decimal,
                FieldType.Double => DbType.Double,
                FieldType.Boolean => DbType.Boolean,
                FieldType.DateTime => DbType.DateTime,
                FieldType.Guid => DbType.Guid,
                FieldType.Binary => DbType.Binary,
                FieldType.Json => DbType.String,
                _ => DbType.String
            };
        }
    }

    /// <summary>
    /// A schema-only bounded string. Derives from <see cref="CharField"/> because that is the type test
    /// <c>ConvertType</c> performs before it will emit a length (TASK-264).
    /// </summary>
    internal sealed class SchemaCharField : CharField
    {
        public SchemaCharField(FieldDescriptor descriptor, int? length)
            : base(null!, SchemaField.ColumnNameOf(descriptor), descriptor.IsPrimary, descriptor.IsUnique, length)
        {
            // CharField's constructor takes neither, so they are assigned rather than passed.
            IsNotNull = descriptor.IsRequired;
            IsAutoincrement = descriptor.IsAutoIncrement;
            Type = SchemaField.MapFieldType(descriptor.Type);
        }

        public override void Read(object value, DbDataReader reader, int index)
        {
            // Schema-only field: Property is null, and CharField.Read would dereference it.
        }
    }

    /// <summary>
    /// A schema-only decimal carrying precision and scale. Without this the connector emits a bare
    /// <c>DECIMAL</c> — <c>DECIMAL(18,0)</c> on SQL Server, so a declared scale was silently dropped and
    /// money was truncated to whole units (TASK-264).
    /// </summary>
    internal sealed class SchemaDecimalField : DecimalField
    {
        public SchemaDecimalField(FieldDescriptor descriptor)
            : base(null!, SchemaField.ColumnNameOf(descriptor), descriptor.IsPrimary, descriptor.IsUnique, descriptor.IsAutoIncrement, descriptor.Precision, descriptor.Scale)
        {
            IsNotNull = descriptor.IsRequired;
        }

        public override void Read(object value, DbDataReader reader, int index)
        {
            // Schema-only field: Property is null, and DecimalField.Read would dereference it.
        }
    }
}
