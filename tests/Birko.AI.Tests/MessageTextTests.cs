using System.Collections.Generic;
using Birko.AI.Models;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Tests
{
    /// <summary>
    /// Covers the canonical text accessor for <see cref="Message.Content"/> (which is <c>object?</c> —
    /// a string for user turns, a <c>List&lt;ContentBlock&gt;</c> for assistant turns). Before this,
    /// a consumer casting <c>Content</c> to string got a block list's CLR type name.
    /// </summary>
    public class MessageTextTests
    {
        [Fact]
        public void GetText_StringContent_ReturnsTheString()
        {
            var message = new Message { Role = "user", Content = "hello world" };

            message.GetText().Should().Be("hello world");
        }

        [Fact]
        public void GetText_SingleTextBlock_ReturnsItsText()
        {
            var message = new Message
            {
                Role = "assistant",
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = "answer" }
                }
            };

            message.GetText().Should().Be("answer");
        }

        [Fact]
        public void GetText_MultipleMixedBlocks_ConcatenatesOnlyTextBlocks()
        {
            var message = new Message
            {
                Role = "assistant",
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = "part one " },
                    new() { Type = "tool_use", Name = "run", Text = null },
                    new() { Type = "text", Text = "part two" }
                }
            };

            message.GetText().Should().Be("part one part two");
        }

        [Fact]
        public void GetText_NoTextBlocks_ReturnsEmpty()
        {
            var message = new Message
            {
                Role = "assistant",
                Content = new List<ContentBlock>
                {
                    new() { Type = "tool_use", Name = "run" }
                }
            };

            message.GetText().Should().BeEmpty();
        }

        [Fact]
        public void GetText_NullContent_ReturnsEmpty()
        {
            var message = new Message { Role = "assistant", Content = null };

            message.GetText().Should().BeEmpty();
        }

        [Fact]
        public void From_SingleContentBlock_ReturnsItsText()
        {
            var block = new ContentBlock { Type = "text", Text = "solo" };

            MessageText.From(block).Should().Be("solo");
        }

        [Fact]
        public void From_UnrecognizedContent_ReturnsEmpty()
        {
            MessageText.From(42).Should().BeEmpty();
        }
    }
}
