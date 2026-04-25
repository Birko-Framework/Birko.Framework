using System;
using Birko.Configuration;
using Birko.Data.Models;
using Microsoft.Azure.Cosmos;

namespace Birko.Data.CosmosDB.Stores
{
    /// <summary>
    /// Azure Cosmos DB-specific settings for database connection.
    /// Extends RemoteSettings with Cosmos DB-specific configuration.
    /// RemoteSettings.Location = endpoint URL, Name = database name,
    /// Password = account key, UserName = container name (optional, defaults to type name).
    /// </summary>
    public class Settings : RemoteSettings, ILoadable<Settings>
    {
        /// <summary>
        /// Gets or sets the partition key path for the container. Default is "/id".
        /// </summary>
        public string PartitionKeyPath { get; set; } = "/id";

        /// <summary>
        /// Gets or sets the request timeout for Cosmos DB operations. Default is 30 seconds.
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets whether to allow bulk execution. Default is true.
        /// </summary>
        public bool AllowBulkExecution { get; set; } = true;

        public Settings() : base() { }

        public Settings(string location, string name, string? password = null, string? containerName = null)
            : base(location, name, containerName ?? string.Empty, password ?? string.Empty, 0, true) { }

        /// <summary>
        /// Creates CosmosClientOptions from the current settings.
        /// </summary>
        public virtual CosmosClientOptions GetCosmosClientOptions()
        {
            return new CosmosClientOptions
            {
                RequestTimeout = RequestTimeout,
                AllowBulkExecution = AllowBulkExecution
            };
        }

        public override string GetId()
        {
            return $"{Location}:{Name}:{UserName}";
        }

        public void LoadFrom(Settings data)
        {
            if (data != null)
            {
                base.LoadFrom((RemoteSettings)data);
                PartitionKeyPath = data.PartitionKeyPath;
                RequestTimeout = data.RequestTimeout;
                AllowBulkExecution = data.AllowBulkExecution;
            }
        }

        public override void LoadFrom(Birko.Configuration.Settings data)
        {
            if (data is Settings cosmosData)
            {
                LoadFrom(cosmosData);
            }
            else
            {
                base.LoadFrom(data);
            }
        }
    }
}
