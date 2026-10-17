using System.Diagnostics;
using System.Runtime.InteropServices;
using Birko.AI;
using Birko.AI.Tools;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Tests
{
    /// <summary>
    /// TASK-160 — the tool and options findings: SH-M167 (search_code and AllowedExternalPaths), SH-M168 (a model-supplied
    /// regex with no timeout and no cancellation), SH-M169 (run_command ignores the exit code), SH-M170 (ask_user reports
    /// cancellation as a timeout) and SH-M171 (AgentOptions.Merge resets unset scalars).
    /// </summary>
    public sealed class Task160ToolAndOptionsTests : IDisposable
    {
        private readonly string _work = Directory.CreateTempSubdirectory("birko_t160_work_").FullName;
        private readonly string _external = Directory.CreateTempSubdirectory("birko_t160_ext_").FullName;

        public void Dispose()
        {
            Directory.Delete(_work, recursive: true);
            Directory.Delete(_external, recursive: true);
        }

        [Fact]
        public async Task SH_M167_search_code_searches_a_granted_external_path()
        {
            File.WriteAllText(Path.Combine(_external, "notes.txt"), "the needle is here");
            var tool = new SearchCodeTool { Options = new AgentOptions { AllowedExternalPaths = [_external] } };

            var result = await tool.ExecuteAsync(_work, new() { ["query"] = "needle", ["directory"] = _external });

            result.Should().Contain("the needle is here");
        }

        [Fact]
        public async Task SH_M167_search_code_still_refuses_an_external_path_nobody_granted()
        {
            var result = await new SearchCodeTool().ExecuteAsync(_work, new() { ["query"] = "x", ["directory"] = _external });
            result.Should().StartWith("Error:");
        }

        [Fact]
        public async Task SH_M168_a_catastrophic_regex_is_stopped_instead_of_pinning_the_cpu()
        {
            File.WriteAllText(Path.Combine(_work, "a.txt"), new string('a', 40) + "!");
            var tool = new SearchCodeTool();

            // The tool works synchronously, so it runs on the pool: without a match timeout it never returns.
            var run = Task.Run(() => tool.ExecuteAsync(_work, new() { ["query"] = "(a+)+$", ["regex"] = true }));
            (await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30)))).Should().Be(run, "the match must time out");

            (await run).Should().StartWith("Error:").And.Contain("timed out");
        }

        [Fact]
        public async Task SH_M168_search_code_observes_the_cancellation_token()
        {
            for (var i = 0; i < 50; i++) File.WriteAllText(Path.Combine(_work, $"f{i}.txt"), "line\n");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = () => new SearchCodeTool().ExecuteAsync(_work, new() { ["query"] = "line" }, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task SH_M169_a_command_that_fails_silently_reports_its_exit_code_as_an_error()
        {
            var windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var result = await new RunCommandTool().ExecuteAsync(_work, new()
            {
                ["command"] = windows ? "cmd" : "sh",
                ["arguments"] = windows ? "/c exit 3" : "-c \"exit 3\"",
            });

            result.Should().StartWith("Error:").And.Contain("3");
        }

        [Fact]
        public async Task SH_M170_ask_user_lets_cancellation_through_instead_of_reporting_a_timeout()
        {
            var never = new TaskCompletionSource<string>();
            var tool = new AskUserTool
            {
                Options = new AgentOptions { Interactive = true, PromptTimeout = 300 },
                PromptCallback = (_, _) => never.Task,
            };
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var watch = Stopwatch.StartNew();

            var act = () => tool.ExecuteAsync(_work, new() { ["question"] = "continue?" }, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public void SH_M171_merging_options_that_set_only_some_fields_leaves_the_rest()
        {
            var target = new AgentOptions { MaxIterations = 50, Verbose = false, Interactive = false, PromptTimeout = 30, ModelDepth = 9 };

            target.Merge(new AgentOptions { WorkingDirectory = "/repo" });

            target.WorkingDirectory.Should().Be("/repo");
            target.MaxIterations.Should().Be(50);
            target.Verbose.Should().BeFalse();
            target.Interactive.Should().BeFalse();
            target.PromptTimeout.Should().Be(30);
            target.ModelDepth.Should().Be(9);
        }

        [Fact]
        public void SH_M171_a_scalar_the_other_sets_still_wins()
        {
            var target = new AgentOptions { MaxIterations = 50 };
            target.Merge(new AgentOptions { MaxIterations = 7, Verbose = false });
            target.MaxIterations.Should().Be(7);
            target.Verbose.Should().BeFalse();
        }
    }
}
