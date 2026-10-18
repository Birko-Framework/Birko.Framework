using System;

namespace Birko.BackgroundJobs
{
    /// <summary>
    /// What every queue does to a descriptor before it stores it (SH-L013).
    /// </summary>
    public static class JobEnqueue
    {
        /// <summary>
        /// A <see cref="JobDescriptor.Delay"/> with no <see cref="JobDescriptor.ScheduledAt"/> becomes ScheduledAt =
        /// EnqueuedAt + Delay, and a Pending job becomes Scheduled. No backend stores Delay, so it was dropped on the
        /// first round trip and the job ran at once; only <c>JobDispatcher.ScheduleAsync</c> resolved it, for its callers.
        /// </summary>
        public static void ResolveDelay(JobDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            if (descriptor.Delay is not { } delay || descriptor.ScheduledAt != null)
            {
                return;
            }
            descriptor.ScheduledAt = descriptor.EnqueuedAt.Add(delay);
            if (descriptor.Status == JobStatus.Pending)
            {
                descriptor.Status = JobStatus.Scheduled;
            }
        }
    }
}
