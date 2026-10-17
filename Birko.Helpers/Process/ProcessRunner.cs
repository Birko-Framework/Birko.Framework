using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Helpers
{
    /// <summary>What to run: the program, its arguments, where, and for how long.</summary>
    /// <param name="FileName">The executable (resolved through PATH, as <see cref="ProcessStartInfo"/> does).</param>
    /// <param name="Arguments">Each argument as one element, passed through <see cref="ProcessStartInfo.ArgumentList"/>: no
    /// quoting and no shell, so an argument cannot become two or an option it was not meant to be.</param>
    public sealed record ProcessRunRequest(string FileName, IReadOnlyList<string> Arguments)
    {
        /// <summary>The working directory; the current one when null.</summary>
        public string? WorkingDirectory { get; init; }

        /// <summary>How long the process may run before its tree is killed; none when null.</summary>
        public TimeSpan? Timeout { get; init; }

        /// <summary>
        /// A raw argument string instead of <see cref="Arguments"/>, split by the platform's rules — only for a caller that
        /// holds one string already (a command line from an agent). Prefer <see cref="Arguments"/>.
        /// </summary>
        public string? RawArguments { get; init; }

        /// <summary>Environment variables to set (a null value removes the variable).</summary>
        public IReadOnlyDictionary<string, string?>? Environment { get; init; }

        /// <summary>Text written to the process's standard input, which is then closed; standard input is not redirected when null.</summary>
        public string? StandardInput { get; init; }
    }

    /// <summary>The outcome of <see cref="ProcessRunner.RunAsync"/>.</summary>
    /// <param name="ExitCode">The exit code; -1 when the process was killed for its timeout.</param>
    /// <param name="TimedOut">True when <see cref="ProcessRunRequest.Timeout"/> elapsed and the process tree was killed.</param>
    public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut, TimeSpan Duration)
    {
        public bool Succeeded => !TimedOut && ExitCode == 0;
    }

    /// <summary>
    /// Runs a process to completion and captures its output (TASK-534) — the one runner, used by <c>RunCommandTool</c> and
    /// the git service. Standard output and standard error are drained concurrently from the start, so a child that
    /// fills one pipe's OS buffer while the other is being read cannot deadlock (DraCode's git runner read stdout to the
    /// end before stderr, and hung on a child writing more than ~64 KB to stderr first).
    /// </summary>
    /// <remarks>
    /// A timeout kills the whole process tree and is reported in the result (<see cref="ProcessRunResult.TimedOut"/>). The
    /// caller's cancellation also kills the tree, and then throws <see cref="OperationCanceledException"/>: it is not a
    /// result. A program that cannot be started throws (<see cref="System.ComponentModel.Win32Exception"/>).
    /// </remarks>
    public static class ProcessRunner
    {
        // After a kill, how long to wait for the pipes to close. A grandchild that escaped the tree kill can hold a pipe
        // open forever; the output read so far is returned instead of hanging the caller.
        private static readonly TimeSpan DrainAfterKill = TimeSpan.FromSeconds(5);

        public static async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
            if (request.Timeout is { } t && t <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(request), t, "A timeout must be positive.");
            cancellationToken.ThrowIfCancellationRequested();

            var psi = new ProcessStartInfo
            {
                FileName = request.FileName,
                WorkingDirectory = request.WorkingDirectory ?? string.Empty,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = request.StandardInput != null,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            if (request.RawArguments != null)
                psi.Arguments = request.RawArguments;
            else
                foreach (var argument in request.Arguments)
                    psi.ArgumentList.Add(argument);
            if (request.Environment != null)
                foreach (var (name, value) in request.Environment)
                {
                    if (value == null) psi.Environment.Remove(name);
                    else psi.Environment[name] = value;
                }

            var started = Stopwatch.GetTimestamp();
            using var process = new System.Diagnostics.Process { StartInfo = psi };
            process.Start();

            var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

            if (request.StandardInput != null)
            {
                try
                {
                    await process.StandardInput.WriteAsync(request.StandardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // The process exited without reading its input; its exit code says what happened.
                }
            }

            using var timeout = request.Timeout is { } limit ? new CancellationTokenSource(limit) : null;
            using var linked = timeout != null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                var (killedOut, killedErr) = await DrainAsync(stdout, stderr).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return new ProcessRunResult(-1, killedOut, killedErr, TimedOut: true, Stopwatch.GetElapsedTime(started));
            }

            var (output, error) = await DrainAsync(stdout, stderr).ConfigureAwait(false);
            return new ProcessRunResult(process.ExitCode, output, error, TimedOut: false, Stopwatch.GetElapsedTime(started));
        }

        private static void Kill(System.Diagnostics.Process process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It exited between the wait and the kill.
            }
        }

        private static async Task<(string Output, string Error)> DrainAsync(Task<string> stdout, Task<string> stderr)
        {
            try
            {
                await Task.WhenAll(stdout, stderr).WaitAsync(DrainAfterKill).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
            return (stdout.IsCompletedSuccessfully ? stdout.Result : string.Empty,
                    stderr.IsCompletedSuccessfully ? stderr.Result : string.Empty);
        }
    }
}
