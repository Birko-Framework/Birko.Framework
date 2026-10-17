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
        /// Refuses a <see cref="PartitionKeyPath"/> the stores cannot address.
        /// </summary>
        /// <remarks>
        /// TASK-614. Every point operation passes <c>new PartitionKey(entity.Guid)</c>, which is the document's
        /// partition key value only when the container is partitioned on <c>/id</c>. Any other path was accepted, the
        /// container was created on it, and then every write failed. Supporting another path means deriving the value
        /// from the entity, and a read or delete by Guid alone could no longer name its partition. That is a feature,
        /// so the knob refuses what the mechanism cannot deliver (rule 10).
        /// </remarks>
        /// <exception cref="NotSupportedException">The path is anything but <c>/id</c>.</exception>
        internal void RequireAddressablePartitionKeyPath()
        {
            if (string.IsNullOrEmpty(PartitionKeyPath) || PartitionKeyPath == "/id")
            {
                return;
            }
            throw new NotSupportedException(
                $"Settings.PartitionKeyPath '{PartitionKeyPath}' is not supported: the Cosmos DB stores address every "
                + "document by its Guid as both id and partition key, which only a container partitioned on '/id' "
                + "accepts. Leave PartitionKeyPath at '/id'.");
        }

        /// <summary>
        /// The path a container is created on: <c>/id</c> whenever <see cref="RequireAddressablePartitionKeyPath"/> accepts the
        /// setting. One producer for both (TASK-650): validation accepted <c>""</c> as "the default" while container creation
        /// fell back only on <c>null</c>, so an empty setting reached Cosmos DB as an empty path.
        /// </summary>
        internal string EffectivePartitionKeyPath => string.IsNullOrEmpty(PartitionKeyPath) ? "/id" : PartitionKeyPath;

        /// <summary>
        /// Gets or sets the request timeout for Cosmos DB operations. Default is 30 seconds.
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets whether to allow bulk execution. Default is true.
        /// </summary>
        public bool AllowBulkExecution { get; set; } = true;

        /// <summary>
        /// Gets or sets how the client reaches the account. Default is
        /// <see cref="Microsoft.Azure.Cosmos.ConnectionMode.Direct"/>, which is also the SDK's default,
        /// so existing consumers are unaffected.
        /// </summary>
        /// <remarks>
        /// <see cref="Microsoft.Azure.Cosmos.ConnectionMode.Gateway"/> routes everything over HTTPS on the
        /// account endpoint instead of opening TCP connections to per-partition replicas. It is slower, and
        /// it is the only mode that works where the Direct-mode port range is blocked — behind a corporate
        /// proxy or a restrictive firewall, and against the Azure Cosmos DB emulator, which serves Gateway
        /// only. Without this setting neither was reachable at all (TASK-223).
        /// </remarks>
        public ConnectionMode ConnectionMode { get; set; } = ConnectionMode.Direct;

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
                AllowBulkExecution = AllowBulkExecution,
                ConnectionMode = ConnectionMode,
                // Ensures every AbstractModel document carries an 'id' == its Guid, so point
                // reads/writes keyed by guid.ToString() resolve against the '/id' partition key (CR-C04).
                Serializer = new Serialization.CosmosGuidIdSerializer()
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
                ConnectionMode = data.ConnectionMode;
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
