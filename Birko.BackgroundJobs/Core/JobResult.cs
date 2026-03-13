using System;

namespace Birko.BackgroundJobs
{
    /// <summary>
    /// Represents the result of a job execution attempt.
    /// </summary>
    public class JobResult
    {
        /// <summary>
        /// Whether the job succeeded.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Error message if the job failed.
        /// </summary>
        public string? Error { get; }

        /// <summary>
        /// Exception that caused the failure, if any.
        /// </summary>
        public Exception? Exception { get; }

        /// <summary>
        /// How long the job took to execute.
        /// </summary>
        public TimeSpan Duration { get; }

        private JobResult(bool success, TimeSpan duration, string? error, Exception? exception)
        {
            Success = success;
            Duration = duration;
            Error = error;
            Exception = exception;
        }

        public static JobResult Succeeded(TimeSpan duration) => new(true, duration, null, null);

        public static JobResult Failed(TimeSpan duration, string error, Exception? exception = null) =>
            new(false, duration, error, exception);
    }
}
