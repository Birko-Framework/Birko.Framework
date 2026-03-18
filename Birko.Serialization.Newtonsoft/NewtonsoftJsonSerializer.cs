using System;
using System.Text;
using Newtonsoft.Json;

namespace Birko.Serialization.Newtonsoft
{
    /// <summary>
    /// JSON serializer using Newtonsoft.Json. Useful when interoperating with APIs or libraries
    /// that require Newtonsoft-specific features (e.g., JsonProperty attributes, custom converters).
    /// </summary>
    public class NewtonsoftJsonSerializer : ISerializer
    {
        private readonly JsonSerializerSettings _settings;

        public NewtonsoftJsonSerializer(JsonSerializerSettings? settings = null)
        {
            _settings = settings ?? new JsonSerializerSettings
            {
                ContractResolver = new global::Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.None
            };
        }

        public string ContentType => "application/json";

        public SerializationFormat Format => SerializationFormat.Json;

        public string Serialize(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return JsonConvert.SerializeObject(value, value.GetType(), _settings);
        }

        public string Serialize<T>(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return JsonConvert.SerializeObject(value, _settings);
        }

        public object? Deserialize(string data, Type type)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(type);
            return JsonConvert.DeserializeObject(data, type, _settings);
        }

        public T? Deserialize<T>(string data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return JsonConvert.DeserializeObject<T>(data, _settings);
        }

        public byte[] SerializeToBytes(object value)
        {
            var json = Serialize(value);
            return Encoding.UTF8.GetBytes(json);
        }

        public byte[] SerializeToBytes<T>(T value)
        {
            var json = Serialize<T>(value);
            return Encoding.UTF8.GetBytes(json);
        }

        public object? DeserializeFromBytes(byte[] data, Type type)
        {
            ArgumentNullException.ThrowIfNull(data);
            var json = Encoding.UTF8.GetString(data);
            return Deserialize(json, type);
        }

        public T? DeserializeFromBytes<T>(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            var json = Encoding.UTF8.GetString(data);
            return Deserialize<T>(json);
        }
    }
}
