using System.Collections.Generic;

namespace Birko.Data.Migrations.Context
{
    public interface IDataMigrator
    {
        void UpdateDocuments(string collection, string filterJson, IDictionary<string, object> updates);
        void DeleteDocuments(string collection, string filterJson);
        long CountDocuments(string collection, string? filterJson = null);
        void CopyData(string sourceCollection, string targetCollection, string? transformJson = null);
        void BulkInsert(string collection, IEnumerable<IDictionary<string, object>> documents);
    }
}
