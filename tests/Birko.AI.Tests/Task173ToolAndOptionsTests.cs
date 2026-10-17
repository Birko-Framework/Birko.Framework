using System.Runtime.InteropServices;
using Birko.AI;
using Birko.AI.Tools;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Tests
{
    /// <summary>
    /// TASK-173 — the low spec-harvest findings in the tools and <see cref="AgentOptions"/>: SH-L160, L162, L166, L167,
    /// L170..L173.
    /// </summary>
    public sealed class Task173ToolAndOptionsTests : IDisposable
    {
        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private readonly string _work = Directory.CreateTempSubdirectory("birko_t173_").FullName;

        public void Dispose()
        {
            foreach (var dir in Directory.EnumerateDirectories(_work, "*", SearchOption.AllDirectories))
                if (!IsWindows) File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(_work, recursive: true);
        }

        // ── AgentOptions ──

        [Fact]
        public void SH_L162_an_empty_working_directory_in_configuration_keeps_the_default()
        {
            AgentOptions.FromDictionary(new() { ["workingDirectory"] = "" }).WorkingDirectory.Should().Be("./");
            ((Action)(() => AgentOptions.FromDictionary(null!))).Should().Throw<ArgumentNullException>();
        }

        // ── run_command ──

        [Fact]
        public async Task SH_L166_a_timed_out_command_returns_what_it_printed_before_the_timeout()
        {
            var result = await new RunCommandTool().ExecuteAsync(_work, new()
            {
                ["command"] = IsWindows ? "cmd" : "sh",
                ["arguments"] = IsWindows ? "/c \"echo partial-output & ping -n 20 127.0.0.1 > nul\"" : "-c \"echo partial-output; sleep 20\"",
                ["timeout_seconds"] = 2,
            });

            result.Should().StartWith("Error: Process timed out").And.Contain("partial-output");
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        public async Task SH_L167_a_non_positive_timeout_is_refused_before_anything_starts(int seconds)
        {
            var marker = Path.Combine(_work, "started.txt");
            var result = await new RunCommandTool().ExecuteAsync(_work, new()
            {
                ["command"] = IsWindows ? "cmd" : "sh",
                ["arguments"] = IsWindows ? $"/c echo x > \"{marker}\"" : $"-c \"echo x > '{marker}'\"",
                ["timeout_seconds"] = seconds,
            });

            result.Should().StartWith("Error:");
            File.Exists(marker).Should().BeFalse();
        }

        // ── display_text ──

        [Fact]
        public async Task SH_L170_display_text_without_a_callback_does_not_claim_the_text_was_shown()
        {
            var result = await new DisplayTextTool().ExecuteAsync(_work, new() { ["text"] = "hello" });
            result.Should().StartWith("Error:");
        }

        [Fact]
        public async Task Display_text_with_a_callback_still_succeeds()
        {
            var shown = new List<string>();
            var result = await new DisplayTextTool { MessageCallback = (_, text) => shown.Add(text) }.ExecuteAsync(_work, new() { ["text"] = "hello" });
            result.Should().NotStartWith("Error:");
            shown.Should().Equal("hello");
        }

        // ── write / edit / append resolve paths like read ──

        [Fact]
        public async Task SH_L171_a_leading_slash_means_workspace_relative_for_every_file_tool()
        {
            Directory.CreateDirectory(Path.Combine(_work, "src"));
            var options = new AgentOptions();

            (await new WriteFileTool { Options = options }.ExecuteAsync(_work, new() { ["file_path"] = "/src/x.cs", ["content"] = "a" }))
                .Should().NotStartWith("Error:");
            (await new AppendToFileTool { Options = options }.ExecuteAsync(_work, new() { ["file_path"] = "/src/x.cs", ["content"] = "b" }))
                .Should().NotStartWith("Error:");
            (await new EditFileTool { Options = options }.ExecuteAsync(_work, new() { ["file_path"] = "/src/x.cs", ["old_text"] = "ab", ["new_text"] = "cd" }))
                .Should().NotStartWith("Error:");
            (await new ReadFileTool { Options = options }.ExecuteAsync(_work, new() { ["file_path"] = "/src/x.cs" })).Should().Be("cd");
        }

        // ── search_code ──

        [Fact]
        public async Task SH_L172_an_unreadable_subdirectory_does_not_discard_the_matches_found()
        {
            if (IsWindows) return; // the permission is set with a Unix mode; Windows ACLs are out of scope here
            File.WriteAllText(Path.Combine(_work, "a.txt"), "needle");
            var locked = Directory.CreateDirectory(Path.Combine(_work, "locked")).FullName;
            File.WriteAllText(Path.Combine(locked, "b.txt"), "needle");
            File.SetUnixFileMode(locked, UnixFileMode.None);

            var search = await new SearchCodeTool().ExecuteAsync(_work, new() { ["query"] = "needle" });
            var list = await new ListFilesTool().ExecuteAsync(_work, new() { ["recursive"] = true });

            search.Should().Contain("a.txt:1: needle");
            list.Should().Contain("a.txt");
        }

        [Fact]
        public async Task SH_L173_no_match_says_so_and_how_many_files_were_searched()
        {
            File.WriteAllText(Path.Combine(_work, "a.txt"), "hay");
            var result = await new SearchCodeTool().ExecuteAsync(_work, new() { ["query"] = "needle" });
            result.Should().Contain("No matches").And.Contain("1 file");
        }

        [Fact]
        public async Task SH_L173_a_pattern_that_matches_no_file_says_so()
        {
            var result = await new SearchCodeTool().ExecuteAsync(_work, new() { ["query"] = "x", ["pattern"] = "*.cs" });
            result.Should().Contain("No matches").And.Contain("0 files");
        }
    }
}
