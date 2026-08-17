using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.Tests.Processing
{
    /// <summary>
    /// TASK-236 — a file-backed lock provider, and the measurement that put it here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TASK-232 guessed the file stores could not lock. The opposite is true and it is the interesting
    /// half: an exclusive file handle is <b>session-scoped</b>, released by the kernel when the holder
    /// dies, which is the guarantee no document store in this family can offer. These tests pin that,
    /// including the part a same-process test cannot reach — killing a holder outright.
    /// </para>
    /// <para>
    /// Nothing here is gated. The whole point of a file lock is that it needs no service.
    /// </para>
    /// </remarks>
    public class FileJobLockProviderTests : IDisposable
    {
        private readonly string _dir;

        public FileJobLockProviderTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), $"birko-task236-{Guid.NewGuid():N}");
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
        }

        [Fact]
        public void A_file_lock_is_session_scoped_not_lease_based()
        {
            using var p = new FileJobLockProvider(_dir);

            ((IJobLockProvider)p).IsLeaseBased.Should().BeFalse(
                "the lock is an open handle, so the operating system releases it when this process dies — " +
                "there is no lease to expire mid-work, which is the stronger of the two guarantees");
        }

        [Fact]
        public async Task A_lease_duration_is_refused_rather_than_silently_ignored()
        {
            using var p = new FileJobLockProvider(_dir);

            var act = () => p.TryAcquireAsync("x", TimeSpan.Zero, TimeSpan.FromMinutes(5));

            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("leaseDuration",
                "accepting a bound this provider cannot enforce is the lie TASK-232 removed from the " +
                "interface; SqlJobLockProvider refuses it for the same reason");
        }

        [Fact]
        public async Task A_second_provider_cannot_take_a_held_lock()
        {
            using var first = new FileJobLockProvider(_dir);
            using var second = new FileJobLockProvider(_dir);

            (await first.TryAcquireAsync("leader", TimeSpan.Zero)).Should().BeTrue();
            (await second.TryAcquireAsync("leader", TimeSpan.Zero)).Should().BeFalse(
                "losing the race is an expected outcome and must return false, not throw");
        }

        [Fact]
        public async Task Different_names_do_not_exclude_each_other()
        {
            using var a = new FileJobLockProvider(_dir);
            using var b = new FileJobLockProvider(_dir);

            (await a.TryAcquireAsync("one", TimeSpan.Zero)).Should().BeTrue();
            (await b.TryAcquireAsync("two", TimeSpan.Zero)).Should().BeTrue(
                "the lock is per name; a provider that excluded everything would pass every other test here");
        }

        [Fact]
        public async Task Releasing_lets_the_next_caller_in()
        {
            using var first = new FileJobLockProvider(_dir);
            using var second = new FileJobLockProvider(_dir);

            await first.TryAcquireAsync("handover", TimeSpan.Zero);
            await first.ReleaseAsync("handover");

            first.IsLocked.Should().BeFalse();
            (await second.TryAcquireAsync("handover", TimeSpan.Zero)).Should().BeTrue();
        }

        [Fact]
        public async Task Releasing_a_lock_that_is_not_held_is_not_an_error()
        {
            using var p = new FileJobLockProvider(_dir);

            var act = () => p.ReleaseAsync("never-held");

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task Disposing_the_holder_frees_the_lock()
        {
            var holder = new FileJobLockProvider(_dir);
            await holder.TryAcquireAsync("crash", TimeSpan.Zero);
            await holder.DisposeAsync();

            using var next = new FileJobLockProvider(_dir);
            (await next.TryAcquireAsync("crash", TimeSpan.Zero)).Should().BeTrue();
        }

        [Fact]
        public async Task Acquire_waits_up_to_the_acquire_timeout_and_then_gives_up()
        {
            using var holder = new FileJobLockProvider(_dir);
            await holder.TryAcquireAsync("busy", TimeSpan.Zero);

            using var waiter = new FileJobLockProvider(_dir);
            var started = DateTime.UtcNow;
            var got = await waiter.TryAcquireAsync("busy", TimeSpan.FromSeconds(1));
            var waited = DateTime.UtcNow - started;

            got.Should().BeFalse();
            waited.Should().BeGreaterThan(TimeSpan.FromMilliseconds(700),
                "acquireTimeout is a wait — an exclusive open does not block, so it polls");
            waited.Should().BeLessThan(TimeSpan.FromSeconds(4), "and it must give up rather than hang");
        }

        [Theory]
        [InlineData("../escape")]
        [InlineData("a/b")]
        [InlineData("a\\b")]
        public async Task A_lock_name_cannot_walk_out_of_the_lock_directory(string name)
        {
            // The name reaches a path, so it is sanitised rather than trusted. Without that,
            // "../escape" would take a lock on — and create a file at — the parent directory.
            using var p = new FileJobLockProvider(_dir);

            (await p.TryAcquireAsync(name, TimeSpan.Zero)).Should().BeTrue();

            // Assert the exact path the UNSANITISED name would have reached, rather than "the parent
            // directory has no .lock files" — the parent here is the shared temp directory, so that
            // weaker assertion fails on anyone else's leftovers and passes for the wrong reason when it
            // does pass.
            var escaped = Path.GetFullPath(Path.Combine(_dir, name + ".lock"));
            File.Exists(escaped).Should().BeFalse(
                "a sanitised name must not reach the path the raw name names");

            var created = Directory.GetFiles(_dir, "*.lock");
            created.Should().HaveCount(1);
            Path.GetFullPath(created[0]).Should().StartWith(Path.GetFullPath(_dir) + Path.DirectorySeparatorChar,
                "the lock file stays inside the directory the provider was given");
        }

        [Fact]
        public async Task A_name_with_nothing_usable_in_it_is_refused()
        {
            using var p = new FileJobLockProvider(_dir);

            var act = () => p.TryAcquireAsync("...", TimeSpan.Zero);

            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("lockName",
                "sanitising '...' to an empty name would otherwise produce a file called '.lock'");
        }

        // ---- the half a same-process test cannot reach ------------------------------------------------

        [Fact]
        public async Task The_os_frees_an_exclusive_handle_when_its_holder_is_killed()
        {
            // PINS THE PREMISE, NOT THE PROVIDER — and the distinction was found by mutation, not by
            // reading. Changing the provider's FileShare.None to FileShare.ReadWrite fails
            // A_second_provider_cannot_take_a_held_lock and Acquire_waits_up_to_the_acquire_timeout, and
            // leaves THIS test green: the holder below opens the file itself, so what is being exercised
            // is the operating system's behaviour rather than this class's share mode.
            //
            // It is worth keeping for exactly that reason. `IsLeaseBased => false` is a claim about the
            // OS, not about our code: if a future runtime or platform stopped releasing the handle on
            // process death, every other test here would still pass while the guarantee had evaporated.
            // This is the one that would notice — the same role as the test asserting that an array's
            // .Contains really does bind MemoryExtensions.
            //
            // Measured for TASK-236 on Windows (taskkill /F) and Linux/.NET 9 (kill -9) before the
            // provider was written; the child dies with no chance to run any cleanup, so Dispose proves
            // nothing here — that is the graceful path.
            Directory.CreateDirectory(_dir);
            var lockPath = Path.Combine(_dir, "killed.lock");

            using var holder = StartHolder(lockPath);
            try
            {
                // Wait for the child to actually hold it.
                var deadline = DateTime.UtcNow.AddSeconds(20);
                using (var probe = new FileJobLockProvider(_dir))
                {
                    bool blocked = false;
                    while (DateTime.UtcNow < deadline)
                    {
                        if (!await probe.TryAcquireAsync("killed", TimeSpan.Zero))
                        {
                            blocked = true;
                            break;
                        }
                        await probe.ReleaseAsync("killed");
                        await Task.Delay(100);
                    }
                    blocked.Should().BeTrue("the child process must have taken the lock");
                }

                holder.Kill(entireProcessTree: true);
                holder.WaitForExit(20_000);
            }
            catch (Exception)
            {
                try { if (!holder.HasExited) holder.Kill(entireProcessTree: true); } catch { }
                throw;
            }

            using var next = new FileJobLockProvider(_dir);
            var acquired = false;
            var until = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < until && !acquired)
            {
                acquired = await next.TryAcquireAsync("killed", TimeSpan.Zero);
                if (!acquired) await Task.Delay(100);
            }

            acquired.Should().BeTrue(
                "the kernel closes the handle when the process dies, so the lock frees with nobody " +
                "having released it — that is what session-scoped means, and it is the guarantee no " +
                "document-store lease can give");
        }

        /// <summary>
        /// Starts a separate process that opens the lock file exclusively and sits on it.
        /// </summary>
        /// <remarks>
        /// Uses the test host's own runtime to run a tiny inline program rather than shelling out to a
        /// platform-specific tool, so the test is the same on Windows and Linux.
        /// </remarks>
        private static Process StartHolder(string lockPath)
        {
            var script = Path.Combine(Path.GetDirectoryName(lockPath)!, "holder.cs");
            File.WriteAllText(script, $@"
using var fs = new System.IO.FileStream(@""{lockPath}"", System.IO.FileMode.OpenOrCreate,
    System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);
System.Threading.Thread.Sleep(120000);
");
            var psi = new ProcessStartInfo("dotnet", $"run --file \"{script}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            return Process.Start(psi)!;
        }
    }
}
