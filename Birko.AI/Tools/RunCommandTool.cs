using System.Diagnostics;

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
                command = new { type = "string", description = "Executable or shell command to run" },
                arguments = new { type = "string", description = "Optional arguments string" },
                timeout_seconds = new { type = "number", description = "Optional timeout in seconds", @default = 120 }
            },
            required = new[] { "command" }
        };

        public override Task<string> ExecuteAsync(string workingDirectory, Dictionary<string, object> input)
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

                var psi = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                    return Task.FromResult("Error: Failed to start process");

                if (!proc.WaitForExit(timeoutSeconds * 1000))
                {
                    try { proc.Kill(true); } catch { }
                    return Task.FromResult("Error: Process timed out");
                }

                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();

                if (!string.IsNullOrEmpty(stderr))
                    return Task.FromResult(stdout.Length > 0 ? stdout + "\n" + stderr : stderr);

                return Task.FromResult(stdout);
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Error running command: {ex.Message}");
            }
        }
    }
}
