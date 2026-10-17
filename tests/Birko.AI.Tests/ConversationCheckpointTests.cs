using System.Text.Json;
using Birko.AI.Models;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Tests
{
    /// <summary>
    /// TASK-535 — <see cref="ConversationCheckpoint"/> and <see cref="ConversationTrimmer"/>. DraCode restored contents as
    /// <see cref="JsonElement"/>, which <see cref="MessageText.From"/> read as "" (so every restored turn had no text), and
    /// trimmed to the last 50 messages, which could keep a tool_result without its tool_use.
    /// </summary>
    public class ConversationCheckpointTests
    {
        private static Message User(string text) => new() { Role = "user", Content = text };
        private static Message Assistant(string text) => new() { Role = "assistant", Content = new List<ContentBlock> { new() { Type = "text", Text = text } } };
        private static Message ToolUse(string id, string name = "read_file") => new()
        {
            Role = "assistant",
            Content = new List<ContentBlock>
            {
                new() { Type = "text", Text = "let me look" },
                new() { Type = "tool_use", Id = id, Name = name, Input = new Dictionary<string, object> { ["path"] = "a.txt", ["lines"] = 3 } },
            },
        };
        // The exact shape Agent appends for tool results.
        private static Message ToolResult(string id, string result) => new()
        {
            Role = "user",
            Content = new List<object> { new { type = "tool_result", tool_use_id = id, content = result } },
        };

        // ── round trip ──

        [Fact]
        public void Every_block_type_round_trips_in_the_agents_in_memory_shapes()
        {
            var conversation = new List<Message> { User("hello"), ToolUse("t1"), ToolResult("t1", "file body"), Assistant("done") };

            var restored = ConversationCheckpoint.Deserialize(ConversationCheckpoint.Serialize(conversation));

            restored.Select(m => m.Role).Should().Equal("user", "assistant", "user", "assistant");
            restored[0].Content.Should().Be("hello");

            var blocks = restored[1].Content.Should().BeOfType<List<ContentBlock>>().Subject;
            blocks[0].Should().BeEquivalentTo(new ContentBlock { Type = "text", Text = "let me look" });
            blocks[1].Type.Should().Be("tool_use");
            blocks[1].Id.Should().Be("t1");
            blocks[1].Name.Should().Be("read_file");
            JsonSerializer.Serialize(blocks[1].Input).Should().Be("{\"path\":\"a.txt\",\"lines\":3}");

            var results = restored[2].Content.Should().BeOfType<List<object>>().Subject;
            var result = results.Single();
            result.GetType().GetProperty("type")!.GetValue(result).Should().Be("tool_result");
            result.GetType().GetProperty("tool_use_id")!.GetValue(result).Should().Be("t1");
            result.GetType().GetProperty("content")!.GetValue(result).Should().Be("file body");

            restored[3].GetText().Should().Be("done");
        }

        [Fact]
        public void A_restored_tool_result_has_the_same_type_the_agent_builds()
        {
            var live = ToolResult("t1", "x").Content as List<object>;
            var restored = ConversationCheckpoint.Deserialize(ConversationCheckpoint.Serialize([ToolResult("t1", "x")]))[0].Content as List<object>;
            restored![0].GetType().GetProperties().Select(p => p.Name).Should().Equal(live![0].GetType().GetProperties().Select(p => p.Name));
        }

        [Fact]
        public void Restored_turns_have_their_text()
        {
            var restored = ConversationCheckpoint.Deserialize(ConversationCheckpoint.Serialize([User("q"), Assistant("a")]));
            restored.Select(m => m.GetText()).Should().Equal("q", "a");
        }

        [Fact]
        public void A_checkpoint_of_another_version_or_shape_is_refused()
        {
            ((Action)(() => ConversationCheckpoint.Deserialize("{\"version\":2,\"messages\":[]}"))).Should().Throw<JsonException>().WithMessage("*version 2*");
            ((Action)(() => ConversationCheckpoint.Deserialize("[]"))).Should().Throw<JsonException>();
        }

        // ── MessageText over JsonElement (what a plain deserializer produces) ──

        [Fact]
        public void Text_is_read_from_a_json_element_content_of_either_shape()
        {
            var plain = JsonSerializer.Deserialize<Message>("{\"role\":\"user\",\"content\":\"hi there\"}")!;
            var blocks = JsonSerializer.Deserialize<Message>("{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"a\"},{\"type\":\"tool_use\",\"id\":\"x\"},{\"type\":\"text\",\"text\":\"b\"}]}")!;

            plain.Content.Should().BeOfType<JsonElement>();
            plain.GetText().Should().Be("hi there");
            blocks.GetText().Should().Be("ab");
        }

        // ── trim ──

        private static List<Message> LongConversation()
        {
            // user0, then 10 rounds of: assistant tool_use, user tool_result; then a final assistant answer.
            var list = new List<Message> { User("task") };
            for (var i = 0; i < 10; i++)
            {
                list.Add(ToolUse($"t{i}"));
                list.Add(ToolResult($"t{i}", $"r{i}"));
            }
            list.Add(Assistant("final"));
            return list;
        }

        private static bool IsToolResult(Message m) => m.Content is List<object> items && items.Count > 0
            && items[0].GetType().GetProperty("type")?.GetValue(items[0]) as string == "tool_result";

        public static TheoryData<int> Limits()
        {
            var data = new TheoryData<int>();
            for (var max = 2; max <= 23; max++) data.Add(max);
            return data;
        }

        [Theory]
        [MemberData(nameof(Limits))]
        public void A_trim_at_any_size_keeps_the_first_user_message_and_never_orphans_a_tool_result(int max)
        {
            var conversation = LongConversation(); // 22 messages
            var trimmed = ConversationTrimmer.Trim(conversation, max);

            trimmed.Count.Should().BeLessThanOrEqualTo(Math.Max(max, 1));
            trimmed[0].Should().BeSameAs(conversation[0]);
            for (var i = 1; i < trimmed.Count; i++)
            {
                if (IsToolResult(trimmed[i]))
                    trimmed[i - 1].Role.Should().Be("assistant", "a kept tool_result needs the tool_use before it");
                (trimmed[i].Role == "user" && trimmed[i - 1].Role == "user").Should().BeFalse("two user turns must not become adjacent");
            }
            trimmed[^1].Should().BeSameAs(conversation[^1]);
        }

        [Fact]
        public void A_conversation_that_fits_is_returned_whole_as_a_copy()
        {
            var conversation = LongConversation();
            var trimmed = ConversationTrimmer.Trim(conversation, 50);
            trimmed.Should().Equal(conversation);
            trimmed.Should().NotBeSameAs(conversation);
        }

        [Fact]
        public void A_limit_under_two_is_refused()
        {
            ((Action)(() => ConversationTrimmer.Trim(LongConversation(), 1))).Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
