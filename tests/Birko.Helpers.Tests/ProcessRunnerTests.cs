using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Birko.Helpers;
using FluentAssertions;
using Xunit;

namespace Birko.Helpers.Tests;

/// <summary>
/// TASK-534 — <see cref="ProcessRunner"/>, the one process runner. DraCode's git runner read stdout to the end before
/// stderr, with no timeout and no cancellation; a child that writes more than an OS pipe buffer (~64 KB) to stderr
/// before finishing stdout deadlocks that order.
/// </summary>
public class ProcessRunnerTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    // A shell command, the same on both platforms' terms.
    private static ProcessRunRequest Shell(string windows, string unix) => IsWindows
        ? new ProcessRunRequest("cmd", ["/c", windows])
        : new ProcessRunRequest("sh", ["-c", unix]);

    [Fact]
    public async Task A_child_writing_far_more_than_a_pipe_buffer_to_stderr_before_stdout_does_not_deadlock()
    {
        // 256 KB to stderr, then a line to stdout. Reading stdout to the end first blocks forever: the child is stuck
        // writing stderr, whose buffer nobody drains, and never reaches stdout's end.
        var request = IsWindows
            ? new ProcessRunRequest("powershell", ["-NoProfile", "-Command", "[Console]::Error.Write('e' * 262144); [Console]::Out.Write('done')"])
            : new ProcessRunRequest("sh", ["-c", "head -c 262144 /dev/zero | tr '\\0' e 1>&2; printf done"]);

        var run = ProcessRunner.RunAsync(request with { Timeout = TimeSpan.FromSeconds(60) });
        (await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(55)))).Should().Be(run, "the runner must not deadlock");

        var result = await run;
        result.TimedOut.Should().BeFalse();
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Be("done");
        result.StandardError.Length.Should().Be(262144);
    }

    [Fact]
    public async Task A_timeout_kills_the_process_and_is_reported()
    {
        var request = Shell("ping -n 30 127.0.0.1 > nul", "sleep 30") with { Timeout = TimeSpan.FromMilliseconds(500) };
        var watch = Stopwatch.StartNew();

        var result = await ProcessRunner.RunAsync(request);

        result.TimedOut.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task The_callers_cancellation_kills_the_process_and_throws()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var watch = Stopwatch.StartNew();

        var act = () => ProcessRunner.RunAsync(Shell("ping -n 30 127.0.0.1 > nul", "sleep 30"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task The_exit_code_is_returned()
    {
        var result = await ProcessRunner.RunAsync(Shell("exit 3", "exit 3"));
        result.ExitCode.Should().Be(3);
        result.Succeeded.Should().BeFalse();
        result.TimedOut.Should().BeFalse();
    }

    [Fact]
    public async Task An_argument_with_spaces_and_quotes_arrives_as_one_argument()
    {
        // git echoes a -c value back exactly as one argument received it.
        var result = await ProcessRunner.RunAsync(new ProcessRunRequest("git", ["-c", "test.value=a b \"c\" -d", "config", "test.value"]));
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Trim().Should().Be("a b \"c\" -d");
    }

    [Fact]
    public async Task Standard_input_is_written_and_closed()
    {
        var result = await ProcessRunner.RunAsync(new ProcessRunRequest("git", ["hash-object", "--stdin"]) { StandardInput = "hello" });
        result.StandardOutput.Trim().Should().Be("b6fc4c620b67d95f953a5c1c1230aaab5db5a1b0");
    }

    [Fact]
    public async Task Environment_variables_are_set_for_the_child()
    {
        var request = Shell("echo %BIRKO_RUNNER_TEST%", "echo $BIRKO_RUNNER_TEST") with
        {
            Environment = new Dictionary<string, string?> { ["BIRKO_RUNNER_TEST"] = "set-by-test" },
        };
        (await ProcessRunner.RunAsync(request)).StandardOutput.Trim().Should().Be("set-by-test");
    }

    [Fact]
    public async Task The_working_directory_is_used()
    {
        var dir = Directory.CreateTempSubdirectory("birko_runner_").FullName;
        try
        {
            var result = await ProcessRunner.RunAsync(Shell("cd", "pwd") with { WorkingDirectory = dir });
            Path.GetFullPath(result.StandardOutput.Trim()).TrimEnd(Path.DirectorySeparatorChar)
                .Should().EndWith(Path.GetFileName(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task A_program_that_does_not_exist_throws()
    {
        var act = () => ProcessRunner.RunAsync(new ProcessRunRequest("birko-no-such-program-534", []));
        await act.Should().ThrowAsync<Win32Exception>();
    }

    [Fact]
    public async Task Bad_requests_are_refused()
    {
        await ((Func<Task>)(() => ProcessRunner.RunAsync(new ProcessRunRequest(" ", [])))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => ProcessRunner.RunAsync(new ProcessRunRequest("git", []) { Timeout = TimeSpan.Zero })))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
