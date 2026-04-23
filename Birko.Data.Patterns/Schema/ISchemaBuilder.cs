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
    }
}
