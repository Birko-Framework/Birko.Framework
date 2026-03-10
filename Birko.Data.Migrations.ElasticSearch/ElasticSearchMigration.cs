using Nest;

namespace Birko.Data.Migrations.ElasticSearch
{
    /// <summary>
    /// Abstract base class for ElasticSearch migrations.
    /// </summary>
    public abstract class ElasticSearchMigration : Data.Migrations.AbstractMigration
    {
        /// <summary>
        /// Applies the migration using the ElasticClient.
        /// </summary>
        /// <param name="client">The ElasticSearch client.</param>
        protected abstract void Up(ElasticClient client);

        /// <summary>
        /// Reverts the migration using the ElasticClient.
        /// </summary>
        /// <param name="client">The ElasticSearch client.</param>
        protected abstract void Down(ElasticClient client);

        /// <summary>
        /// Throws exception - migrations require ElasticClient context.
        /// </summary>
        public override void Up()
        {
            throw new System.InvalidOperationException("ElasticSearchMigration requires an ElasticClient. Use ElasticSearchMigrationRunner to execute migrations.");
        }

        /// <summary>
        /// Throws exception - migrations require ElasticClient context.
        /// </summary>
        public override void Down()
        {
            throw new System.InvalidOperationException("ElasticSearchMigration requires an ElasticClient. Use ElasticSearchMigrationRunner to execute migrations.");
        }

        /// <summary>
        /// Internal execution method called by ElasticSearchMigrationRunner.
        /// </summary>
        internal void Execute(ElasticClient client, Data.Migrations.MigrationDirection direction)
        {
            if (direction == Data.Migrations.MigrationDirection.Up)
            {
                Up(client);
            }
            else
            {
                Down(client);
            }
        }

        /// <summary>
        /// Creates an index with the specified descriptor.
        /// </summary>
        protected virtual void CreateIndex(ElasticClient client, string indexName, Func<CreateIndexDescriptor, ICreateIndexRequest> descriptor)
        {
            if (!client.Indices.Exists(indexName).Exists)
            {
                var response = client.Indices.Create(indexName, descriptor);
                if (!response.IsValid)
                {
                    throw new System.InvalidOperationException($"Failed to create index {indexName}: {response.DebugInformation}", response.OriginalException);
                }
            }
        }

        /// <summary>
        /// Deletes an index.
        /// </summary>
        protected virtual void DeleteIndex(ElasticClient client, string indexName)
        {
            if (client.Indices.Exists(indexName).Exists)
            {
                var response = client.Indices.Delete(indexName);
                if (!response.IsValid)
                {
                    throw new System.InvalidOperationException($"Failed to delete index {indexName}: {response.DebugInformation}", response.OriginalException);
                }
            }
        }

        /// <summary>
        /// Creates or updates an index alias.
        /// </summary>
        protected virtual void CreateAlias(ElasticClient client, string indexName, string aliasName)
        {
            var response = client.Indices.PutAlias(indexName, aliasName);
            if (!response.IsValid)
            {
                throw new System.InvalidOperationException($"Failed to create alias {aliasName} on {indexName}: {response.DebugInformation}", response.OriginalException);
            }
        }

        /// <summary>
        /// Deletes an index alias.
        /// </summary>
        protected virtual void DeleteAlias(ElasticClient client, string indexName, string aliasName)
        {
            var response = client.Indices.DeleteAlias(indexName, aliasName);
            if (!response.IsValid && !response.ServerError?.Status.ToString().StartsWith("4") == true)
            {
                throw new System.InvalidOperationException($"Failed to delete alias {aliasName} from {indexName}: {response.DebugInformation}", response.OriginalException);
            }
        }

        /// <summary>
        /// Reindexes data from one index to another.
        /// </summary>
        protected virtual void Reindex(ElasticClient client, string sourceIndex, string targetIndex)
        {
            var reindexResponse = client.ReindexOnServer(r => r
                .Source(s => s.Index(sourceIndex))
                .Destination(d => d.Index(targetIndex))
                .WaitForCompletion(true)
            );

            if (!reindexResponse.IsValid)
            {
                throw new System.InvalidOperationException($"Reindex failed from {sourceIndex} to {targetIndex}: {reindexResponse.DebugInformation}", reindexResponse.OriginalException);
            }

            // Refresh the target index
            client.Indices.Refresh(targetIndex);
        }

        /// <summary>
        /// Updates index mapping.
        /// </summary>
        protected virtual void UpdateMapping<T>(ElasticClient client, string indexName, Func<PutMappingDescriptor<T>, IPutMappingRequest> mappingDescriptor) where T : class
        {
            var response = client.Map(mappingDescriptor);
            if (!response.IsValid)
            {
                throw new System.InvalidOperationException($"Failed to update mapping for {indexName}: {response.DebugInformation}", response.OriginalException);
            }
        }

        /// <summary>
        /// Checks if an index exists.
        /// </summary>
        protected virtual bool IndexExists(ElasticClient client, string indexName)
        {
            return client.Indices.Exists(indexName).Exists;
        }

        /// <summary>
        /// Creates an index template.
        /// </summary>
        protected virtual void PutTemplate(ElasticClient client, string templateName, Func<PutIndexTemplateDescriptor, IPutIndexTemplateRequest> templateDescriptor)
        {
            var response = client.Indices.PutTemplate(templateName, templateDescriptor);
            if (!response.IsValid)
            {
                throw new System.InvalidOperationException($"Failed to create template {templateName}: {response.DebugInformation}", response.OriginalException);
            }
        }

        /// <summary>
        /// Deletes an index template.
        /// </summary>
        protected virtual void DeleteTemplate(ElasticClient client, string templateName)
        {
            var response = client.Indices.DeleteTemplate(templateName);
            if (!response.IsValid && !response.ServerError?.Status.ToString().StartsWith("4") == true)
            {
                throw new System.InvalidOperationException($"Failed to delete template {templateName}: {response.DebugInformation}", response.OriginalException);
            }
        }
    }
}
