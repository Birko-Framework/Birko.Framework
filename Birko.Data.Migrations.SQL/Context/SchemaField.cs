using System.Data;
using System.Data.Common;
using Birko.Data.Patterns.Schema;
using Birko.Data.SQL.Fields;

namespace Birko.Data.Migrations.SQL.Context
{
    internal class SchemaField : AbstractField
    {
        public SchemaField(FieldDescriptor descriptor)
            : base(null!, descriptor.Name, MapFieldType(descriptor.Type), descriptor.IsPrimary, descriptor.IsRequired, descriptor.IsUnique, descriptor.IsAutoIncrement)
        {
        }

        public override void Read(object value, DbDataReader reader, int index)
        {
            // Schema-only field, not used for data operations
        }

        private static DbType MapFieldType(FieldType type)
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
}
