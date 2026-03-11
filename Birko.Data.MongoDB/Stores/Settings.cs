using System;
using System.Collections.Generic;

namespace Birko.Data.MongoDB.Stores
{
    /// <summary>
    /// MongoDB-specific settings for database connection.
    /// </summary>
    public class Settings : Data.Stores.Settings, Data.Models.ILoadable<Settings>
    {
        /// <summary>
        /// Gets or sets the username for authentication.
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// Gets or sets the password for authentication.
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Gets or sets the port number (default: 27017).
        /// </summary>
        public int Port { get; set; } = 27017;

        /// <summary>
        /// Gets or sets the authentication database name (default: admin).
        /// </summary>
        public string AuthDatabase { get; set; } = "admin";

        /// <summary>
        /// Gets or sets the replica set name for replica set connections.
        /// </summary>
        public string ReplicaSet { get; set; }

        /// <summary>
        /// Gets or sets whether to use TLS/SSL for the connection.
        /// </summary>
        public bool UseTls { get; set; } = false;

        /// <summary>
        /// Initializes a new instance of the Settings class.
        /// </summary>
        public Settings() : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the Settings class.
        /// </summary>
        /// <param name="location">The server location/hostname.</param>
        /// <param name="name">The database name.</param>
        /// <param name="username">The username for authentication.</param>
        /// <param name="password">The password for authentication.</param>
        public Settings(string location, string name, string username = null, string password = null) : base(location, name)
        {
            UserName = username;
            Password = password;
        }

        /// <summary>
        /// Gets the MongoDB connection string based on the current settings.
        /// </summary>
        /// <returns>A MongoDB connection string.</returns>
        public virtual string GetConnectionString()
        {
            var connectionString = "mongodb://";

            // Add credentials if provided
            if (!string.IsNullOrEmpty(UserName) && !string.IsNullOrEmpty(Password))
            {
                connectionString += $"{UserName}:{Password}@";
            }

            // Add server and port
            connectionString += $"{Location}:{Port}";

            // Add database name
            if (!string.IsNullOrEmpty(Name))
            {
                connectionString += $"/{Name}";
            }

            // Add query parameters
            var queryParams = new List<string>();

            if (!string.IsNullOrEmpty(AuthDatabase))
            {
                queryParams.Add($"authSource={AuthDatabase}");
            }

            if (!string.IsNullOrEmpty(ReplicaSet))
            {
                queryParams.Add($"replicaSet={ReplicaSet}");
            }

            if (UseTls)
            {
                queryParams.Add("tls=true");
            }

            if (queryParams.Count > 0)
            {
                connectionString += "?" + string.Join("&", queryParams);
            }

            return connectionString;
        }

        /// <inheritdoc />
        public override string GetId()
        {
            return $"{Location}:{Port}:{Name}:{UserName}";
        }

        /// <summary>
        /// Loads settings from another Settings instance.
        /// </summary>
        /// <param name="data">The settings to load from.</param>
        public void LoadFrom(Settings data)
        {
            if (data != null)
            {
                base.LoadFrom(data);
                UserName = data.UserName;
                Password = data.Password;
                Port = data.Port;
                AuthDatabase = data.AuthDatabase;
                ReplicaSet = data.ReplicaSet;
                UseTls = data.UseTls;
            }
        }
    }
}
