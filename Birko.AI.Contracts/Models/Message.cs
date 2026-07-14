using System.Text.Json.Serialization;

namespace Birko.AI.Models
{
    public class Message
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public object? Content { get; set; }

        /// <summary>
        /// The plain text of this message's <see cref="Content"/>, whether it is a string (user
        /// turns) or a <c>List&lt;ContentBlock&gt;</c> (assistant turns — the <c>type == "text"</c>
        /// blocks concatenated). Use this instead of casting <see cref="Content"/> to <c>string</c>,
        /// which yields a block list's CLR type name. See <see cref="MessageText"/>.
        /// </summary>
        public string GetText() => MessageText.From(Content);
    }
}
