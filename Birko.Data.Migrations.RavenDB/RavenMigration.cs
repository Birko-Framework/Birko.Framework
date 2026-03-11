using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Operations.Indexes;
using Raven.Client.Documents.Session;
using System;
using System.Linq;

namespace Birko.Data.Migrations.RavenDB
{
    /// <summary>
    /// Abstract base class for RavenDB migrations.
    /// </summary>
    public abstract class RavenMigration : Data.Migrations.AbstractMigration
    {
        /// <summary>
        /// Applies the migration using the RavenDB document store.
        /// </summary>
        /// <param name="store">The RavenDB document store.</param>
        protected abstract void Up(IDocumentStore store);

        /// <summary>
        /// Reverts the migration using the RavenDB document store.
        /// </summary>
        /// <param name="store">The RavenDB document store.</param>
        protected abstract void Down(IDocumentStore store);

        /// <summary>
        /// Throws exception - migrations require IDocumentStore context.
        /// </summary>
        public override void Up()
        {
            throw new InvalidOperationException("RavenMigration requires IDocumentStore. Use RavenMigrationRunner to execute migrations.");
        }

        /// <summary>
        /// Throws exception - migrations require IDocumentStore context.
        /// </summary>
        public override void Down()
        {
            throw new InvalidOperationException("RavenMigration requires IDocumentStore. Use RavenMigrationRunner to execute migrations.");
        }

        /// <summary>
        /// Internal execution method called by RavenMigrationRunner.
        /// </summary>
        internal void Execute(IDocumentStore store, Data.Migrations.MigrationDirection direction)
        {
            if (direction == Data.Migrations.MigrationDirection.Up)
            {
                Up(store);
            }
            else
            {
                Down(store);
            }
        }

        /// <summary>
        /// Creates an index in the database.
        /// </summary>
        protected virtual void CreateIndex(IDocumentStore store, AbstractIndexCreationTask index)
        {
            index.Execute(store);
        }

        /// <summary>
        /// Deploys an index definition to the database.
        /// </summary>
        protected virtual void DeployIndex(IDocumentStore store, IndexDefinition indexDefinition)
        {
            store.Maintenance.Send(new PutIndexesOperation(new[] { indexDefinition }));
        }

        /// <summary>
        /// Deletes an index from the database.
        /// </summary>
        protected virtual void DeleteIndex(IDocumentStore store, string indexName)
        {
            store.Maintenance.Send(new DeleteIndexOperation(indexName));
        }

        /// <summary>
        /// Updates documents matching a predicate using a bulk operation.
        /// </summary>
        protected virtual void UpdateDocuments<T>(IDocumentStore store, string indexName, string predicate, string script) where T : class
        {
            var operation = store.Operations.Send(new PatchByQueryOperation(
                new Raven.Client.Documents.Queries.IndexQuery
                {
                    Query = $"FROM INDEX '{indexName}' WHERE {predicate} UPDATE {{ {script} }}"
                }));

            operation.WaitForCompletion();
        }

        /// <summary>
        /// Deletes documents matching a predicate.
        /// </summary>
        protected virtual void DeleteDocumentsByQuery(IDocumentStore store, string indexName, string predicate = "")
        {
            var query = string.IsNullOrEmpty(predicate)
                ? $"FROM INDEX '{indexName}'"
                : $"FROM INDEX '{indexName}' WHERE {predicate}";

            var operation = store.Operations.Send(new DeleteByQueryOperation(
                new Raven.Client.Documents.Queries.IndexQuery { Query = query }));

            operation.WaitForCompletion();
        }

        /// <summary>
        /// Loads a document by ID.
        /// </summary>
        protected virtual T? LoadDocument<T>(IDocumentStore store, string id) where T : class
        {
            using var session = store.OpenSession();
            return session.Load<T>(id);
        }

        /// <summary>
        /// Stores a document.
        /// </summary>
        protected virtual void StoreDocument(IDocumentStore store, object entity)
        {
            using var session = store.OpenSession();
            session.Store(entity);
            session.SaveChanges();
        }

        /// <summary>
        /// Deletes a document by ID.
        /// </summary>
        protected virtual void DeleteDocument(IDocumentStore store, string id)
        {
            using var session = store.OpenSession();
            session.Delete(id);
            session.SaveChanges();
        }

        /// <summary>
        /// Checks if a document exists.
        /// </summary>
        protected virtual bool DocumentExists(IDocumentStore store, string id)
        {
            using var session = store.OpenSession();
            return session.Advanced.Exists(id);
        }

        /// <summary>
        /// Executes a bulk operation on multiple documents.
        /// </summary>
        protected virtual void BulkInsert<T>(IDocumentStore store, System.Collections.Generic.IEnumerable<T> entities) where T : class
        {
            using var bulkInsert = store.BulkInsert();
            foreach (var entity in entities)
            {
                bulkInsert.Store(entity);
            }
        }
    }
}
