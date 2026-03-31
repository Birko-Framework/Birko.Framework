using System.Text.Json.Serialization;

namespace Birko.AI.Models
{
    public class Message
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public object? Content { get; set; }
    }
}
