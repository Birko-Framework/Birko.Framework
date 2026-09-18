using System.Runtime.InteropServices;
using Birko.AI.Tools;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Tests.Tools
{
    public class RunCommandToolTests
    {
        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        [Fact]
        public async Task ExecuteAsync_LargeStdout_DoesNotDeadlockAndReturnsFullOutput()
        {
            // Regression for CR-H001: RunCommandTool used to WaitForExit FIRST and only read
            // StandardOutput/StandardError afterwards. Once a child fills the OS pipe buffer
            // (~64KB) it blocks on write and never exits, so WaitForExit hits the timeout and
            // the output is lost. Emit far more than a pipe buffer's worth and assert we get it
            // all back, without timing out.
            var line = new string('x', 1000);
            const int lineCount = 500; // ~500 KB, well beyond any pipe buffer
            var bigFile = Path.Combine(Path.GetTempPath(), $"crh001-{Guid.NewGuid():N}.txt");
            await File.WriteAllLinesAsync(bigFile, Enumerable.Repeat(line, lineCount));
            try
            {
                var tool = new RunCommandTool();
                var input = new Dictionary<string, object>
                {
                    ["command"] = IsWindows ? "cmd" : "cat",
                    ["arguments"] = IsWindows ? $"/c type \"{bigFile}\"" : $"\"{bigFile}\"",
                    ["timeout_seconds"] = 30
                };

                var task = tool.ExecuteAsync(Directory.GetCurrentDirectory(), input);
                var winner = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(25)));
                winner.Should().Be(task, "the command must not deadlock on large output");

                var result = await task;
                result.Should().NotContain("timed out");
                (result.Split(line).Length - 1).Should().BeGreaterThanOrEqualTo(lineCount);
            }
            finally
            {
                File.Delete(bigFile);
            }
        }

        [Fact]
        public async Task ExecuteAsync_EchoesSmallOutput()
        {
            var tool = new RunCommandTool();
            var input = new Dictionary<string, object>
            {
                ["command"] = IsWindows ? "cmd" : "echo",
                ["arguments"] = IsWindows ? "/c echo hello-crh001" : "hello-crh001",
                ["timeout_seconds"] = 15
            };

            var result = await tool.ExecuteAsync(Directory.GetCurrentDirectory(), input);

            result.Should().Contain("hello-crh001");
        }

        [Fact]
        public async Task ExecuteAsync_MissingCommand_ReturnsError()
        {
            var tool = new RunCommandTool();
            var input = new Dictionary<string, object> { ["command"] = "   " };

            var result = await tool.ExecuteAsync(Directory.GetCurrentDirectory(), input);

            result.Should().StartWith("Error");
        }
    }
}
