using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;
using Birko.BackgroundJobs.Serialization;
using Birko.Time;

namespace Birko.Framework.Examples.BackgroundJobs
{
    // --- Sample job definitions ---

    public class CleanupJob : IJob
    {
        public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            ExampleOutput.WriteInfo("CleanupJob", $"Running (attempt #{context.AttemptNumber}, job {context.JobId:N})");
            await Task.Delay(100, cancellationToken);
            ExampleOutput.WriteSuccess("CleanupJob completed - cleaned up temp files");
        }
    }

    public class ReportInput
    {
        public string ReportName { get; set; } = string.Empty;
        public string Format { get; set; } = "PDF";
        public int Year { get; set; }
    }

    public class GenerateReportJob : IJob<ReportInput>
    {
        public async Task ExecuteAsync(ReportInput input, JobContext context, CancellationToken cancellationToken = default)
        {
            ExampleOutput.WriteInfo("GenerateReportJob", $"Generating {input.ReportName} ({input.Format}) for {input.Year}...");
            await Task.Delay(200, cancellationToken);
            ExampleOutput.WriteSuccess("Report generated successfully");
        }
    }

    public class HealthCheckJob : IJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            ExampleOutput.WriteSuccess($"System healthy at {DateTime.UtcNow:HH:mm:ss} (recurring: {context.Metadata.GetValueOrDefault("recurring.name", "n/a")})");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Examples demonstrating the Birko.BackgroundJobs framework.
    /// </summary>
    public static class BackgroundJobsExamples
    {
        /// <summary>
        /// InMemoryJobQueue + JobDispatcher: enqueue, schedule, cancel, check status.
        /// </summary>
        public static async Task RunDispatcherExample()
        {
            ExampleOutput.WriteHeader("Job Dispatcher Example");
            ExampleOutput.WriteLine();

            var clock = new SystemDateTimeProvider();
            var queue = new InMemoryJobQueue(clock);
            var dispatcher = new JobDispatcher(queue, clock);

            // Enqueue a parameterless job
            var cleanupId = await dispatcher.EnqueueAsync<CleanupJob>();
            ExampleOutput.WriteSuccess($"Enqueued CleanupJob: {cleanupId}");

            // Enqueue a job with typed input
            var reportId = await dispatcher.EnqueueAsync<GenerateReportJob, ReportInput>(
                new ReportInput { ReportName = "Annual Sales", Format = "PDF", Year = 2026 });
            ExampleOutput.WriteSuccess($"Enqueued GenerateReportJob: {reportId}");

            // Schedule a job for later
            var scheduledId = await dispatcher.ScheduleAsync<CleanupJob>(TimeSpan.FromMinutes(30));
            ExampleOutput.WriteInfo("Scheduled", $"CleanupJob in 30 min: {scheduledId}");

            // Enqueue on a specific queue with priority
            var urgentId = await dispatcher.EnqueueWithPriorityAsync<CleanupJob>(priority: 10);
            ExampleOutput.WriteInfo("Priority", $"High-priority CleanupJob: {urgentId}");

            var queuedId = await dispatcher.EnqueueOnAsync<CleanupJob>("maintenance");
            ExampleOutput.WriteInfo("Queue", $"CleanupJob on 'maintenance': {queuedId}");

            // Check status
            var status = await dispatcher.GetStatusAsync(cleanupId);
            ExampleOutput.WriteInfo("Status", $"CleanupJob: {status}");

            // Cancel the scheduled job
            var cancelled = await dispatcher.CancelAsync(scheduledId);
            ExampleOutput.WriteInfo("Cancel", $"Scheduled job: {cancelled}");
            var cancelledStatus = await dispatcher.GetStatusAsync(scheduledId);
            ExampleOutput.WriteInfo("Status", $"After cancel: {cancelledStatus}");

            // List pending jobs
            ExampleOutput.WriteLine();
            var pending = await queue.GetByStatusAsync(JobStatus.Pending);
            ExampleOutput.WriteMarkupLine($"[bold]Pending jobs: {pending.Count}[/]");
            foreach (var job in pending)
            {
                ExampleOutput.WriteDim($"  {job.JobType.Split(',')[0].Split('.').Last()} (priority: {job.Priority}, queue: {job.QueueName ?? "default"})");
            }
        }

        /// <summary>
        /// BackgroundJobProcessor: process jobs from the queue with concurrency control.
        /// </summary>
        public static async Task RunProcessorExample()
        {
            ExampleOutput.WriteHeader("Job Processor Example");
            ExampleOutput.WriteLine();

            var clock = new SystemDateTimeProvider();
            var queue = new InMemoryJobQueue(clock);
            var serializer = new JsonJobSerializer();
            var executor = new JobExecutor(type => Activator.CreateInstance(type)!, serializer);

            // Enqueue several jobs
            var dispatcher = new JobDispatcher(queue, clock, serializer);
            await dispatcher.EnqueueAsync<CleanupJob>();
            await dispatcher.EnqueueAsync<GenerateReportJob, ReportInput>(
                new ReportInput { ReportName = "Q1 Revenue", Format = "Excel", Year = 2026 });
            await dispatcher.EnqueueAsync<CleanupJob>();

            ExampleOutput.WriteInfo("Enqueued", "3 jobs. Starting processor...");
            ExampleOutput.WriteLine();

            // Configure processor
            var options = new JobQueueOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(200),
                MaxConcurrency = 2,
                JobTimeout = TimeSpan.FromSeconds(30)
            };

            var processor = new BackgroundJobProcessor(queue, executor, options);

            // Run processor for a short time
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            ExampleOutput.WriteDim("Processor running (max concurrency: 2)...");
            ExampleOutput.WriteLine();
            await processor.RunAsync(cts.Token);

            // Check results
            ExampleOutput.WriteLine();
            ExampleOutput.WriteMarkupLine("[bold]Results:[/]");
            var completed = await queue.GetByStatusAsync(JobStatus.Completed);
            ExampleOutput.WriteSuccess($"Completed: {completed.Count}");
            var pending = await queue.GetByStatusAsync(JobStatus.Pending);
            ExampleOutput.WriteInfo("Pending", $"{pending.Count}");
        }

        /// <summary>
        /// RecurringJobScheduler: register jobs that fire at fixed intervals.
        /// </summary>
        public static async Task RunRecurringSchedulerExample()
        {
            ExampleOutput.WriteHeader("Recurring Job Scheduler Example");
            ExampleOutput.WriteLine();

            var clock = new SystemDateTimeProvider();
            var queue = new InMemoryJobQueue(clock);
            var scheduler = new RecurringJobScheduler(queue, clock);

            // Register recurring jobs
            scheduler.Register<HealthCheckJob>("health-check", TimeSpan.FromSeconds(2));
            scheduler.Register<CleanupJob>("daily-cleanup", TimeSpan.FromSeconds(3), queueName: "maintenance");
            ExampleOutput.WriteMarkupLine("[bold]Registered recurring jobs:[/]");
            ExampleOutput.WriteInfo("health-check", "every 2 seconds");
            ExampleOutput.WriteInfo("daily-cleanup", "every 3 seconds (maintenance queue)");
            ExampleOutput.WriteLine();

            // Run scheduler for a short time
            ExampleOutput.WriteDim("Running scheduler for 5 seconds...");
            ExampleOutput.WriteLine();

            // Run scheduler and processor together
            var serializer = new JsonJobSerializer();
            var executor = new JobExecutor(type => Activator.CreateInstance(type)!, serializer);
            var processor = new BackgroundJobProcessor(queue, executor, new JobQueueOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(200),
                MaxConcurrency = 2
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var schedulerTask = scheduler.RunAsync(cts.Token);
            var processorTask = processor.RunAsync(cts.Token);

            await Task.WhenAll(schedulerTask, processorTask);

            // Results
            var completed = await queue.GetByStatusAsync(JobStatus.Completed);
            ExampleOutput.WriteLine();
            ExampleOutput.WriteSuccess($"Total jobs completed: {completed.Count}");

            // Remove a recurring job
            var removed = scheduler.Remove("health-check");
            ExampleOutput.WriteInfo("Removed", $"'health-check': {removed}");
        }

        /// <summary>
        /// RetryPolicy: exponential backoff and custom retry strategies.
        /// </summary>
        public static void RunRetryPolicyExample()
        {
            ExampleOutput.WriteHeader("Retry Policy Example");
            ExampleOutput.WriteLine();

            // Default policy
            var defaultPolicy = RetryPolicy.Default;
            ExampleOutput.WriteMarkupLine("[bold]Default RetryPolicy:[/]");
            ExampleOutput.WriteInfo("MaxRetries", $"{defaultPolicy.MaxRetries}");
            ExampleOutput.WriteInfo("BaseDelay", $"{defaultPolicy.BaseDelay}");
            ExampleOutput.WriteInfo("MaxDelay", $"{defaultPolicy.MaxDelay}");
            ExampleOutput.WriteInfo("ExponentialBackoff", $"{defaultPolicy.UseExponentialBackoff}");
            ExampleOutput.WriteLine();

            ExampleOutput.WriteMarkupLine("[bold]Exponential backoff delays:[/]");
            for (int i = 1; i <= 6; i++)
            {
                ExampleOutput.WriteDim($"  Attempt {i}: {defaultPolicy.GetDelay(i)}");
            }
            ExampleOutput.WriteLine();

            // Fixed delay
            var fixedPolicy = new RetryPolicy
            {
                MaxRetries = 5,
                BaseDelay = TimeSpan.FromSeconds(10),
                UseExponentialBackoff = false
            };
            ExampleOutput.WriteMarkupLine("[bold]Fixed delay policy:[/]");
            for (int i = 1; i <= 3; i++)
            {
                ExampleOutput.WriteDim($"  Attempt {i}: {fixedPolicy.GetDelay(i)}");
            }
            ExampleOutput.WriteLine();

            // No retries
            var noRetry = RetryPolicy.None;
            ExampleOutput.WriteInfo("No-retry policy", $"MaxRetries = {noRetry.MaxRetries}");

            // Custom aggressive retry
            var aggressive = new RetryPolicy
            {
                MaxRetries = 10,
                BaseDelay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromMinutes(5),
                UseExponentialBackoff = true
            };
            ExampleOutput.WriteLine();
            ExampleOutput.WriteMarkupLine("[bold]Aggressive retry (10 attempts, 1s base, 5m cap):[/]");
            for (int i = 1; i <= 10; i++)
            {
                ExampleOutput.WriteDim($"  Attempt {i}: {aggressive.GetDelay(i)}");
            }
        }

        /// <summary>
        /// JobQueueOptions: processor configuration overview.
        /// </summary>
        public static void RunConfigurationExample()
        {
            ExampleOutput.WriteHeader("Background Jobs Configuration");
            ExampleOutput.WriteLine();

            var options = new JobQueueOptions();
            ExampleOutput.WriteMarkupLine("[bold]JobQueueOptions defaults:[/]");
            ExampleOutput.WriteInfo("PollingInterval", $"{options.PollingInterval}");
            ExampleOutput.WriteInfo("MaxConcurrency", $"{options.MaxConcurrency}");
            ExampleOutput.WriteInfo("DefaultQueueName", $"{options.DefaultQueueName}");
            ExampleOutput.WriteInfo("JobTimeout", $"{options.JobTimeout}");
            ExampleOutput.WriteInfo("RetentionPeriod", $"{options.RetentionPeriod}");
            ExampleOutput.WriteInfo("RetryPolicy", $"{options.RetryPolicy.MaxRetries} retries, {options.RetryPolicy.BaseDelay} base delay");

            ExampleOutput.WriteLine();
            ExampleOutput.WriteMarkupLine("[bold]JobStatus lifecycle:[/]");
            ExampleOutput.WriteSuccess("Pending → Processing → Completed");
            ExampleOutput.WriteWarning("Pending → Processing → Failed → Scheduled (retry) → Processing → ...");
            ExampleOutput.WriteError("Pending → Processing → Failed → Dead (max retries)");
            ExampleOutput.WriteDim("Pending → Cancelled");
            ExampleOutput.WriteDim("Scheduled → Processing (when ScheduledAt ≤ now)");

            ExampleOutput.WriteLine();
            ExampleOutput.WriteMarkupLine("[bold]Available queue backends:[/]");
            ExampleOutput.WriteInfo("InMemoryJobQueue", "testing/development (non-persistent)");
            ExampleOutput.WriteInfo("SqlJobQueue<DB>", "SQL Server, PostgreSQL, MySQL, SQLite");
            ExampleOutput.WriteInfo("ElasticSearchJobQueue", "Elasticsearch");
            ExampleOutput.WriteInfo("MongoDBJobQueue", "MongoDB");
            ExampleOutput.WriteInfo("RavenDBJobQueue", "RavenDB");
            ExampleOutput.WriteInfo("JsonJobQueue", "JSON file (dev/testing)");

            ExampleOutput.WriteLine();
            ExampleOutput.WriteDim("All backends implement IJobQueue with identical API.");
            ExampleOutput.WriteDim("Swap backends by changing the queue instance — no code changes needed.");
        }
    }
}
