namespace Birko.Serialization
{
    /// <summary>
    /// Identifies the serialization format.
    /// </summary>
    public enum SerializationFormat
    {
        /// <summary>
        /// JSON format (System.Text.Json or Newtonsoft.Json).
        /// </summary>
        Json,

        /// <summary>
        /// MessagePack binary format.
        /// </summary>
        MessagePack,

        /// <summary>
        /// Protocol Buffers binary format.
        /// </summary>
        Protobuf,

        /// <summary>
        /// XML format (System.Xml.Serialization).
        /// </summary>
        Xml,

        /// <summary>
        /// YAML format (YamlDotNet).
        /// </summary>
        Yaml
    }
}
