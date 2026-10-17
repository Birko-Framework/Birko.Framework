using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Birko.AI.Models
{
    /// <summary>
    /// Saves a conversation to JSON and restores it in the shapes the agent builds in memory (TASK-535), so a restored
    /// conversation can be resumed (<c>Agent.ResumeAsync</c>) and read (<see cref="Message.GetText"/>) like a live one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The format is <c>{"version":1,"messages":[{"role":…,"content":…}]}</c>. A content is a string, or an array of blocks:
    /// <c>{"type":"text","text"}</c>, <c>{"type":"tool_use","id","name","input"}</c>,
    /// <c>{"type":"tool_result","tool_use_id","content"}</c>.
    /// </para>
    /// <para>
    /// Restored shapes: a string stays a string; text and tool_use blocks become <c>List&lt;ContentBlock&gt;</c>; tool_result
    /// blocks become the same anonymous <c>{ type, tool_use_id, content }</c> objects <c>Agent</c> appends, which every
    /// provider reads. DraCode restored contents as <see cref="JsonElement"/>, and <see cref="MessageText.From"/> had no
    /// case for it, so every restored turn read as "".
    /// </para>
    /// </remarks>
    public static class ConversationCheckpoint
    {
        public const int Version = 1;

        public static string Serialize(IEnumerable<Message> messages)
        {
            var array = new JsonArray();
            foreach (var message in messages)
                array.Add(new JsonObject { ["role"] = message.Role, ["content"] = ContentToNode(message.Content) });
            return new JsonObject { ["version"] = Version, ["messages"] = array }.ToJsonString();
        }

        /// <exception cref="JsonException">Not a checkpoint, or a version this code does not read.</exception>
        public static List<Message> Deserialize(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) || !root.TryGetProperty("messages", out var messages))
                throw new JsonException("Not a conversation checkpoint.");
            if (version.GetInt32() != Version)
                throw new JsonException($"Conversation checkpoint version {version.GetInt32()} is not supported (expected {Version}).");

            var result = new List<Message>();
            foreach (var m in messages.EnumerateArray())
            {
                var role = m.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                var content = m.TryGetProperty("content", out var c) ? ContentFromJson(c) : null;
                result.Add(new Message { Role = role, Content = content });
            }
            return result;
        }

        /// <summary>
        /// Converts a JSON content (as a checkpoint, or another serializer, stored it) to the in-memory shape: a string, a
        /// <c>List&lt;ContentBlock&gt;</c>, or the agent's tool_result objects.
        /// </summary>
        public static object? ContentFromJson(JsonElement content)
        {
            switch (content.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return null;
                case JsonValueKind.String:
                    return content.GetString();
                case JsonValueKind.Array:
                    var items = content.EnumerateArray().ToList();
                    if (items.Count > 0 && items.All(i => TypeOf(i) == "tool_result"))
                    {
                        return items.Select(i => (object)new
                        {
                            type = "tool_result",
                            tool_use_id = Str(i, "tool_use_id"),
                            content = i.TryGetProperty("content", out var c) ? (c.ValueKind == JsonValueKind.String ? c.GetString() : c.GetRawText()) : null,
                        }).ToList();
                    }
                    return items.Select(BlockFromJson).ToList();
                default:
                    return content.GetRawText();
            }
        }

        private static ContentBlock BlockFromJson(JsonElement block) => new()
        {
            Type = TypeOf(block),
            Text = Str(block, "text"),
            Id = Str(block, "id"),
            Name = Str(block, "name"),
            Input = block.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object
                ? input.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone())
                : null,
        };

        private static JsonNode? ContentToNode(object? content)
        {
            switch (content)
            {
                case null:
                    return null;
                case string s:
                    return JsonValue.Create(s);
                case JsonElement element:
                    return JsonNode.Parse(element.GetRawText());
                case ContentBlock block:
                    return new JsonArray(BlockToNode(block));
                case IEnumerable<ContentBlock> blocks:
                    return new JsonArray(blocks.Select(b => (JsonNode?)BlockToNode(b)).ToArray());
                case IEnumerable<object> items:
                    return new JsonArray(items.Select(ItemToNode).ToArray());
                default:
                    return JsonSerializer.SerializeToNode(content);
            }
        }

        private static JsonObject BlockToNode(ContentBlock block)
        {
            var node = new JsonObject { ["type"] = block.Type };
            if (block.Text != null) node["text"] = block.Text;
            if (block.Id != null) node["id"] = block.Id;
            if (block.Name != null) node["name"] = block.Name;
            if (block.Input != null) node["input"] = JsonSerializer.SerializeToNode(block.Input);
            return node;
        }

        // An item of a non-ContentBlock list: the agent's anonymous tool_result objects, or anything serializable.
        private static JsonNode? ItemToNode(object? item) => item switch
        {
            null => null,
            ContentBlock block => BlockToNode(block),
            JsonElement element => JsonNode.Parse(element.GetRawText()),
            _ => JsonSerializer.SerializeToNode(item, item.GetType()),
        };

        private static string? TypeOf(JsonElement block) => Str(block, "type");

        private static string? Str(JsonElement element, string name)
            => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    /// <summary>
    /// Shortens a conversation to at most <c>maxMessages</c> without breaking it (TASK-535): the first user message is
    /// kept, and the kept tail starts at an assistant turn, so a tool_result is never kept without the tool_use it
    /// answers and two user turns never become adjacent. DraCode kept the last 50 messages, which could drop a tool_use
    /// and keep its tool_result, and providers reject that.
    /// </summary>
    public static class ConversationTrimmer
    {
        /// <returns>A new list; the input is not modified. The whole conversation when it already fits.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="maxMessages"/> is under 2.</exception>
        public static List<Message> Trim(IReadOnlyList<Message> messages, int maxMessages)
        {
            System.ArgumentNullException.ThrowIfNull(messages);
            if (maxMessages < 2)
                throw new System.ArgumentOutOfRangeException(nameof(maxMessages), maxMessages, "A trimmed conversation keeps the first user message and at least one more.");
            if (messages.Count <= maxMessages) return messages.ToList();

            var first = -1;
            for (var i = 0; i < messages.Count; i++)
                if (messages[i].Role == "user") { first = i; break; }
            if (first < 0) return messages.Skip(messages.Count - maxMessages).ToList();

            // The tail may hold maxMessages - 1; move its start forward to an assistant turn.
            var start = System.Math.Max(first + 1, messages.Count - (maxMessages - 1));
            while (start < messages.Count && messages[start].Role != "assistant") start++;

            var result = new List<Message> { messages[first] };
            result.AddRange(messages.Skip(start));
            return result;
        }
    }
}
