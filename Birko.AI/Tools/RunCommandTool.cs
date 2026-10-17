using Birko.Helpers;

namespace Birko.AI.Tools
{
    public class RunCommandTool : Tool
    {
        public override string Name => "run_command";
        public override string Description => "Run a command in the workspace directory and capture output.";
        public override object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                // SH-L168: no shell runs it — builtins, pipes and redirection need an explicit shell.
                command = new { type = "string", description = "Executable to run (no shell: for builtins, pipes or redirection run a shell, e.g. 'cmd' with '/c …' or 'sh' with '-c \"…\"')" },
                arguments = new { type = "string", description = "Optional arguments string" },
                timeout_seconds = new { type = "number", description = "Optional timeout in seconds", @default = 120 }
            },
            required = new[] { "command" }
        };

        // Runs on ProcessRunner (TASK-534), the one process runner: both pipes drained concurrently, the tree killed on
        // timeout. The caller's cancellation propagates; it used to come back as "Error: Process timed out".
        public override async Task<string> ExecuteAsync(string workingDirectory, Dictionary<string, object> input, CancellationToken cancellationToken = default)
        {
            try
            {
                var command = input["command"].ToString();
                var arguments = input.TryGetValue("arguments", out object? value) ? value?.ToString() ?? string.Empty : string.Empty;
                var timeoutSeconds = input.TryGetValue("timeout_seconds", out object? tval) && int.TryParse(tval?.ToString(), out var t) ? t : 120;

                if (string.IsNullOrWhiteSpace(command))
                    throw new ArgumentException("command is required");

                if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
                    throw new ArgumentException("Invalid working directory");

                if (timeoutSeconds <= 0)
                    throw new ArgumentException("timeout_seconds must be positive");

                var result = await ProcessRunner.RunAsync(new ProcessRunRequest(command, [])
                {
                    RawArguments = arguments,
                    WorkingDirectory = workingDirectory,
                    Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                }, cancellationToken);

                // SH-L166: what the process printed before the timeout is the useful part; it was thrown away.
                if (result.TimedOut)
                {
                    var printed = string.Join("\n", new[] { result.StandardOutput, result.StandardError }.Where(s => s.Length > 0));
                    return "Error: Process timed out" + (printed.Length > 0 ? "\n" + printed : "");
                }

                // SH-M169: a failing command with no output used to read as success ("").
                if (result.ExitCode != 0)
                {
                    var output = string.Join("\n", new[] { result.StandardOutput, result.StandardError }.Where(s => s.Length > 0));
                    return $"Error: the command exited with code {result.ExitCode}." + (output.Length > 0 ? "\n" + output : "");
                }

                if (!string.IsNullOrEmpty(result.StandardError))
                    return result.StandardOutput.Length > 0 ? result.StandardOutput + "\n" + result.StandardError : result.StandardError;

                return result.StandardOutput;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                return $"Error: running command failed: {ex.Message}";
            }
        }
    }
}
