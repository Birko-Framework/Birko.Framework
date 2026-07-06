using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.XML.Stores
{
    /// <summary>
    /// XML file-based bulk data store that stores entities in batched files.
    /// Entities are grouped into batch files based on the configured batch size.
    /// Derives from <see cref="XmlBatchStore{T}"/> so it inherits the batched List&lt;T&gt;
    /// persistence (CR-C21) and the batch-settings wiring; it adds nothing of its own.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class XmlBatchBulkStore<T>
        : XmlBatchStore<T>
        , ISettingsStore<Settings>
        , ISettingsStore<ISettings>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Initializes a new instance of the XmlBatchBulkStore class.
        /// </summary>
        public XmlBatchBulkStore() : base()
        {
        }
    }
}
