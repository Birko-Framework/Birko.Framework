using Birko.Data.Models;

namespace Birko.Configuration
{
    #region Settings Interface

    /// <summary>
    /// Interface for store settings.
    /// </summary>
    public interface ISettings : ILoadable<ISettings>
    {
        /// <summary>
        /// Gets the unique identifier for this settings configuration.
        /// </summary>
        /// <returns>The unique identifier string.</returns>
        string GetId();
    }

    #endregion

    #region Base Settings Class

    /// <summary>
    /// Base settings class for file-based data stores.
    /// </summary>
    public class Settings
        : ISettings
        , ILoadable<Settings>
    {
        #region Properties

        /// <summary>
        /// Gets or sets the directory location for the data files.
        /// </summary>
        public string Location { get; set; } = null!;

        /// <summary>
        /// Gets or sets the file name pattern for the data files.
        /// </summary>
        public string Name { get; set; } = null!;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the Settings class.
        /// </summary>
        public Settings() { }

        /// <summary>
        /// Initializes a new instance with specified location and name.
        /// </summary>
        /// <param name="location">The directory location.</param>
        /// <param name="name">The file name pattern.</param>
        public Settings(string location, string name) : this()
        {
            Location = location;
            Name = name;
        }

        #endregion

        #region ISettings Implementation

        /// <inheritdoc />
        public virtual string GetId()
        {
            return string.Format("{0}:{1}", Location, Name);
        }

        #endregion

        #region ILoadable Implementation

        /// <summary>
        /// Loads settings from another Settings instance.
        /// </summary>
        /// <param name="data">The settings to load from.</param>
        public virtual void LoadFrom(Settings data)
        {
            if (data != null)
            {
                Location = data.Location;
                Name = data.Name;
            }
        }

        /// <summary>
        /// Loads settings from an ISettings instance.
        /// </summary>
        /// <param name="data">The settings to load from.</param>
        public void LoadFrom(ISettings data)
        {
            LoadFrom((Settings)data);
        }

        #endregion
    }

    #endregion

    #region Password Settings Class

    /// <summary>
    /// Settings class for data stores that require authentication.
    /// </summary>
    public class PasswordSettings : Settings, ILoadable<PasswordSettings>
    {
        #region Properties

        /// <summary>
        /// Gets or sets the password for authentication.
        /// </summary>
        public string Password { get; set; } = null!;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the PasswordSettings class.
        /// </summary>
        public PasswordSettings() : base() { }

        /// <summary>
        /// Initializes a new instance with all parameters.
        /// </summary>
        /// <param name="location">The directory location.</param>
        /// <param name="name">The file name pattern.</param>
        /// <param name="password">The authentication password.</param>
        public PasswordSettings(string location, string name, string? password = null) : base(location, name)
        {
            Password = password ?? null!;
        }

        #endregion

        #region ILoadable Implementation

        /// <summary>
        /// Loads settings from another PasswordSettings instance.
        /// </summary>
        /// <param name="data">The settings to load from.</param>
        public void LoadFrom(PasswordSettings data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                Password = data.Password;
            }
        }

        public override void LoadFrom(Settings data)
        {
            if (data is PasswordSettings passwordData)
            {
                LoadFrom(passwordData);
            }
        }

        #endregion
    }

    #endregion

    #region Remote Settings Class

    /// <summary>
    /// Settings class for remote data stores that require connection details.
    /// </summary>
    public class RemoteSettings : PasswordSettings, ILoadable<RemoteSettings>
    {
        #region Properties

        /// <summary>
        /// Gets or sets the username for authentication.
        /// </summary>
        public string UserName { get; set; } = null!;

        /// <summary>
        /// Gets or sets the port number for the connection.
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Gets or sets whether to use a secure (SSL/TLS) connection.
        /// </summary>
        public bool UseSecure { get; set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the RemoteSettings class.
        /// </summary>
        public RemoteSettings() : base() { }

        /// <summary>
        /// Initializes a new instance with all connection parameters.
        /// </summary>
        /// <param name="location">The server location or host.</param>
        /// <param name="name">The database or service name.</param>
        /// <param name="username">The authentication username.</param>
        /// <param name="password">The authentication password.</param>
        /// <param name="port">The connection port.</param>
        /// <param name="useSecure">Whether to use a secure (SSL/TLS) connection.</param>
        public RemoteSettings(string location, string name, string username, string password, int port, bool useSecure = false) : base(location, name, password)
        {
            UserName = username;
            Port = port;
            UseSecure = useSecure;
        }

        #endregion

        #region ISettings Implementation

        /// <inheritdoc />
        public override string GetId()
        {
            return string.Format("{0}:{1}:{2}", base.GetId(), UserName, Port);
        }

        #endregion

        #region ILoadable Implementation

        /// <summary>
        /// Loads settings from another RemoteSettings instance.
        /// </summary>
        /// <param name="data">The settings to load from.</param>
        public void LoadFrom(RemoteSettings data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                UserName = data.UserName;
                Port = data.Port;
                UseSecure = data.UseSecure;
            }
        }

        public override void LoadFrom(Settings data)
        {
            if (data is RemoteSettings remoteData)
            {
                LoadFrom(remoteData);
            }
        }

        #endregion
    }

    #endregion
}
