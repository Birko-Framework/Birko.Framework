using System;

using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.JSON.Stores
{
    /// <summary>
    /// Settings for JSON batch stores that split data into multiple files.
    /// Inherits from <see cref="Settings"/> and adds batch size configuration.
    /// </summary>
    public class BatchSettings : Settings
    {
        #region Properties

        /// <summary>
        /// Gets or sets the maximum number of entities to store in each batch file.
        /// Default is 1024.
        /// </summary>
        public int BatchSize { get; set; } = 1024;

        #endregion
    }
}
