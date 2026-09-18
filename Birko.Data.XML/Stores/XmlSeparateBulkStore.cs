using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Birko.Helpers;

using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.XML.Stores
{
    /// <summary>
    /// XML file-based bulk data store that stores each entity in a separate file.
    /// Files are named using the pattern: {Name}-{Guid}.xml
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class XmlSeparateBulkStore<T>
        : XmlSeparateStore<T>
        , ISettingsStore<Settings>
        where T : Models.AbstractModel
    {
        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance of the XmlSeparateBulkStore class.
        /// </summary>
        public XmlSeparateBulkStore() : base()
        {
        }

        #endregion
    }
}
