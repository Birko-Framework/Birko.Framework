using Birko.Data.Stores;
using Birko.Data.SQL.Stores;

namespace Birko.Data.SQL.TimescaleDB.Stores
{
    /// <summary>
    /// Settings for TimescaleDB connections.
    /// Extends SqlSettings with TimescaleDB-specific hypertable configuration.
    /// </summary>
    public class TimescaleDBSettings : SqlSettings, Models.ILoadable<TimescaleDBSettings>
    {
        #region Properties

        /// <summary>
        /// Gets or sets the name of the time column used for hypertable partitioning.
        /// Defaults to "timestamp".
        /// </summary>
        public string TimeColumn { get; set; } = "timestamp";

        /// <summary>
        /// Gets or sets the chunk time interval for hypertable partitioning.
        /// Defaults to "7 days".
        /// </summary>
        public string ChunkTimeInterval { get; set; } = "7 days";

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the TimescaleDBSettings class.
        /// </summary>
        public TimescaleDBSettings() : base() { }

        /// <summary>
        /// Initializes a new instance with all connection parameters.
        /// </summary>
        /// <param name="location">The server location or host.</param>
        /// <param name="name">The database or service name.</param>
        /// <param name="username">The authentication username.</param>
        /// <param name="password">The authentication password.</param>
        /// <param name="port">The connection port.</param>
        /// <param name="timeColumn">The time column name for hypertable partitioning.</param>
        /// <param name="chunkTimeInterval">The chunk time interval for hypertable partitioning.</param>
        public TimescaleDBSettings(string location, string name, string username, string password, int port, string timeColumn = "timestamp", string chunkTimeInterval = "7 days")
            : base(location, name, username, password, port)
        {
            TimeColumn = timeColumn;
            ChunkTimeInterval = chunkTimeInterval;
        }

        public override string GetConnectionString()
        {
            var cs = $"Host={Location};Port={Port};Username={UserName};Password={Password};Database={Name};Timeout={ConnectionTimeout};Command Timeout={CommandTimeout};";
            if (UseSecure)
            {
                cs += "SSL Mode=Require;";
            }
            return cs;
        }

        #endregion

        #region ILoadable Implementation

        /// <summary>
        /// Loads values from another TimescaleDBSettings instance.
        /// </summary>
        /// <param name="data">The settings to load from.</param>
        public void LoadFrom(TimescaleDBSettings data)
        {
            if (data != null)
            {
                base.LoadFrom((SqlSettings)data);
                TimeColumn = data.TimeColumn;
                ChunkTimeInterval = data.ChunkTimeInterval;
            }
        }

        #endregion
    }
}
