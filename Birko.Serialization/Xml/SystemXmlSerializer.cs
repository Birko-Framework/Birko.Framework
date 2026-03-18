using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace Birko.Serialization.Xml
{
    /// <summary>
    /// XML serializer using System.Xml.Serialization (built-in, no external dependencies).
    /// </summary>
    /// <remarks>
    /// Types must be public with a parameterless constructor. Use [XmlRoot], [XmlElement],
    /// [XmlAttribute], [XmlIgnore] attributes to control the XML shape.
    /// </remarks>
    public class SystemXmlSerializer : ISerializer
    {
        private readonly XmlWriterSettings _writerSettings;
        private readonly XmlReaderSettings _readerSettings;

        public SystemXmlSerializer(XmlWriterSettings? writerSettings = null, XmlReaderSettings? readerSettings = null)
        {
            _writerSettings = writerSettings ?? new XmlWriterSettings
            {
                Indent = false,
                OmitXmlDeclaration = false,
                Encoding = new UTF8Encoding(false)
            };
            _readerSettings = readerSettings ?? new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
        }

        public string ContentType => "application/xml";

        public SerializationFormat Format => SerializationFormat.Xml;

        public string Serialize(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var serializer = new XmlSerializer(value.GetType());
            using var stringWriter = new StringWriter();
            using var xmlWriter = XmlWriter.Create(stringWriter, _writerSettings);
            serializer.Serialize(xmlWriter, value);
            return stringWriter.ToString();
        }

        public string Serialize<T>(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var serializer = new XmlSerializer(typeof(T));
            using var stringWriter = new StringWriter();
            using var xmlWriter = XmlWriter.Create(stringWriter, _writerSettings);
            serializer.Serialize(xmlWriter, value);
            return stringWriter.ToString();
        }

        public object? Deserialize(string data, Type type)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(type);
            var serializer = new XmlSerializer(type);
            using var stringReader = new StringReader(data);
            using var xmlReader = XmlReader.Create(stringReader, _readerSettings);
            return serializer.Deserialize(xmlReader);
        }

        public T? Deserialize<T>(string data)
        {
            ArgumentNullException.ThrowIfNull(data);
            var serializer = new XmlSerializer(typeof(T));
            using var stringReader = new StringReader(data);
            using var xmlReader = XmlReader.Create(stringReader, _readerSettings);
            return (T?)serializer.Deserialize(xmlReader);
        }

        public byte[] SerializeToBytes(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var serializer = new XmlSerializer(value.GetType());
            using var stream = new MemoryStream();
            using var xmlWriter = XmlWriter.Create(stream, _writerSettings);
            serializer.Serialize(xmlWriter, value);
            xmlWriter.Flush();
            return stream.ToArray();
        }

        public byte[] SerializeToBytes<T>(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var serializer = new XmlSerializer(typeof(T));
            using var stream = new MemoryStream();
            using var xmlWriter = XmlWriter.Create(stream, _writerSettings);
            serializer.Serialize(xmlWriter, value);
            xmlWriter.Flush();
            return stream.ToArray();
        }

        public object? DeserializeFromBytes(byte[] data, Type type)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(type);
            var serializer = new XmlSerializer(type);
            using var stream = new MemoryStream(data);
            using var xmlReader = XmlReader.Create(stream, _readerSettings);
            return serializer.Deserialize(xmlReader);
        }

        public T? DeserializeFromBytes<T>(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            var serializer = new XmlSerializer(typeof(T));
            using var stream = new MemoryStream(data);
            using var xmlReader = XmlReader.Create(stream, _readerSettings);
            return (T?)serializer.Deserialize(xmlReader);
        }
    }
}
