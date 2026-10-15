using Birko.Data.Patterns.IndexManagement;

namespace Birko.Data.Patterns.Schema
{
    public interface ISchemaBuilder
    {
        ICollectionBuilder CreateCollection(string name);
        void DropCollection(string name);
        bool CollectionExists(string name);
        IIndexBuilder CreateIndex(string collectionName, string indexName);
        void DropIndex(string collectionName, string indexName);
        void AddField(string collectionName, FieldDescriptor field);
        void DropField(string collectionName, string fieldName);
        void RenameField(string collectionName, string oldName, string newName);

        /// <summary>
        /// The <see cref="CreateCollection"/> / <see cref="CreateIndex"/> chains this builder handed out that were never
        /// finished with <c>Build()</c>. A migration runner fails a migration that leaves any, before recording it
        /// (TASK-627). Empty for a builder that does not track (<see cref="SchemaBuildLedger"/>).
        /// </summary>
        System.Collections.Generic.IReadOnlyList<string> PendingBuilds => System.Array.Empty<string>();
    }
}
