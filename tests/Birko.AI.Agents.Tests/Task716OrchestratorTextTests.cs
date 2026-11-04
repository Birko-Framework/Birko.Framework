using System.Collections.Generic;
using System.Text.Json;
using Birko.AI.Agents;
using Birko.AI.Models;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Agents.Tests
{
    /// <summary>
    /// TASK-716. Measured before the fix: <c>OrchestratorAgent.ExtractTextFromContent</c> answered a single non-text block's
    /// <c>Text</c> (a list of the same block answered nothing), and a single JSON block object came back as its raw JSON.
    /// </summary>
    public class Task716OrchestratorTextTests
    {
        [Fact]
        public void A_single_non_text_block_has_no_text()
        {
            OrchestratorAgent.ExtractTextFromContent(new ContentBlock { Type = "thinking", Text = "private reasoning" })
                .Should().BeEmpty();
        }

        [Fact]
        public void A_single_text_block_is_its_text()
        {
            OrchestratorAgent.ExtractTextFromContent(new ContentBlock { Type = "text", Text = "answer" }).Should().Be("answer");
        }

        [Fact]
        public void A_single_json_block_object_is_read_as_a_block()
        {
            var text = JsonSerializer.Deserialize<JsonElement>("""{"type":"text","text":"answer"}""");
            var tool = JsonSerializer.Deserialize<JsonElement>("""{"type":"tool_use","id":"t1","name":"x","input":{}}""");

            OrchestratorAgent.ExtractTextFromContent(text).Should().Be("answer");
            OrchestratorAgent.ExtractTextFromContent(tool).Should().BeEmpty();
        }
    }
}
