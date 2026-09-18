using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Helpers;

using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.XML.Stores
{
    /// <summary>
    /// Async XML file-based bulk data store that stores each entity in a separate file.
    /// Files are named using the pattern: {Name}-{Guid}.xml
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class AsyncXmlSeparateBulkStore<T>
        : AsyncXmlSeparateStore<T>
        , ISettingsStore<Settings>
        where T : Models.AbstractModel
    {
        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance of the AsyncXmlSeparateBulkStore class.
        /// </summary>
        public AsyncXmlSeparateBulkStore() : base()
        {
        }

        #endregion
    }
}
