using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs;

namespace Birko.BackgroundJobs.Tests
{
    public class SuccessJob : IJob
    {
        public static int ExecutionCount { get; set; }

        public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return Task.CompletedTask;
        }
    }

    public class FailingJob : IJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Job failed intentionally");
        }
    }

    public class SlowJob : IJob
    {
        public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    public class EmailInput
    {
        public string To { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
    }

    public class SendEmailJob : IJob<EmailInput>
    {
        public static EmailInput? LastInput { get; set; }
        public static JobContext? LastContext { get; set; }

        public Task ExecuteAsync(EmailInput input, JobContext context, CancellationToken cancellationToken = default)
        {
            LastInput = input;
            LastContext = context;
            return Task.CompletedTask;
        }
    }

    // Contract violation used to regress CR-L019: a typed job whose ExecuteAsync returns a null Task.
#pragma warning disable CS8603 // deliberate null Task return for the test
    public class NullTaskJob : IJob<EmailInput>
    {
        public Task ExecuteAsync(EmailInput input, JobContext context, CancellationToken cancellationToken = default)
            => null!;
    }
#pragma warning restore CS8603

    public class ContextCapturingJob : IJob
    {
        public static JobContext? LastContext { get; set; }

        public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            LastContext = context;
            return Task.CompletedTask;
        }
    }
}
