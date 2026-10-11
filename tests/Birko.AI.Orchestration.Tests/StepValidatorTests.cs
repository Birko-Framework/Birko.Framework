using Birko.AI.Orchestration.Models;
using Birko.AI.Orchestration.Validation;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Orchestration.Tests
{
    /// <summary>
    /// TASK-528 — the step validators against a real temporary directory.
    /// </summary>
    public sealed class StepValidatorTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "birko_steps_" + Guid.NewGuid().ToString("N"));

        public StepValidatorTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* not the assertion */ }
        }

        private string Write(string name, string content)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path, content);
            return path;
        }

        // ── FileCreation ──

        [Fact]
        public async Task FileCreation_passes_for_existing_files_relative_or_rooted()
        {
            Write("a.cs", "x");
            var rooted = Write("b.cs", "x");
            var step = new ImplementationStep { FilesToCreate = { "a.cs", rooted } };

            var result = await new FileCreationValidator().ValidateAsync(step, _dir);

            result.Success.Should().BeTrue();
            result.ValidatorName.Should().Be("FileCreation");
        }

        [Fact]
        public async Task FileCreation_names_each_missing_file()
        {
            Write("a.cs", "x");
            var step = new ImplementationStep { FilesToCreate = { "a.cs", "missing.cs" } };

            var result = await new FileCreationValidator().ValidateAsync(step, _dir);

            result.Success.Should().BeFalse();
            result.Issues.Should().Equal("Expected file not created: missing.cs");
        }

        // ── FileModification ──

        [Fact]
        public async Task FileModification_requires_a_write_after_the_step_started()
        {
            var path = Write("a.cs", "old");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-10));
            var step = new ImplementationStep { FilesToModify = { "a.cs", "gone.cs" } };
            step.Start();

            var before = await new FileModificationValidator().ValidateAsync(step, _dir);
            before.Issues.Should().Equal("File not modified since step started: a.cs", "Expected file not found: gone.cs");

            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            step.FilesToModify.Remove("gone.cs");
            (await new FileModificationValidator().ValidateAsync(step, _dir)).Success.Should().BeTrue();
        }

        // ── ContentExpectation ──

        [Fact]
        public async Task ContentExpectation_passes_when_every_string_is_in_some_step_file()
        {
            Write("a.cs", "public User GetUserById(int id)");
            Write("b.cs", "async Task<User> Load()");
            var step = new ImplementationStep
            {
                FilesToCreate = { "a.cs" },
                FilesToModify = { "b.cs" },
                ExpectedContent = { "GetUserById", "async Task<User>", "  " },
            };

            (await new ContentExpectationValidator().ValidateAsync(step, _dir)).Success.Should().BeTrue();
        }

        [Fact]
        public async Task ContentExpectation_reports_what_is_missing_and_matches_case_sensitively()
        {
            Write("a.cs", "public User getuserbyid(int id)");
            var step = new ImplementationStep { FilesToCreate = { "a.cs" }, ExpectedContent = { "GetUserById" } };

            var result = await new ContentExpectationValidator().ValidateAsync(step, _dir);

            result.Issues.Should().Equal("Expected content not found: 'GetUserById' in [a.cs]");
        }

        [Fact]
        public async Task ContentExpectation_with_nothing_declared_passes()
        {
            var step = new ImplementationStep { FilesToCreate = { "nothing.cs" } };
            (await new ContentExpectationValidator().ValidateAsync(step, _dir)).Success.Should().BeTrue();
        }

        [Fact]
        public async Task ContentExpectation_declared_without_files_is_not_a_pass()
        {
            // DraCode's validator passed this: "can't validate, pass". Could-not-check is not checked.
            var step = new ImplementationStep { ExpectedContent = { "GetUserById" } };

            var result = await new ContentExpectationValidator().ValidateAsync(step, _dir);

            result.Success.Should().BeFalse();
            result.Issues.Should().ContainSingle().Which.Should().Contain("names no files");
        }

        [Fact]
        public async Task ContentExpectation_in_a_file_that_does_not_exist_is_not_found()
        {
            var step = new ImplementationStep { FilesToCreate = { "missing.cs" }, ExpectedContent = { "X" } };

            var result = await new ContentExpectationValidator().ValidateAsync(step, _dir);

            result.Issues.Should().Equal("Expected content not found: 'X' in [missing.cs]");
        }

        // ── the service ──

        [Fact]
        public async Task The_service_combines_the_default_validators_and_counts_one_attempt()
        {
            Write("a.cs", "nothing useful");
            var step = new ImplementationStep { FilesToCreate = { "a.cs", "b.cs" }, ExpectedContent = { "Wanted" } };
            var service = new StepValidationService();

            var result = await service.ValidateStepAsync(step, _dir);

            service.Validators.Select(v => v.Name).Should().Equal("FileCreation", "FileModification", "ContentExpectation");
            result.Success.Should().BeFalse();
            result.IndividualResults.Should().HaveCount(3);
            result.AllIssues.Should().Equal(
                "[FileCreation] Expected file not created: b.cs",
                "[ContentExpectation] Expected content not found: 'Wanted' in [a.cs, b.cs]");
            step.Metrics.ValidationAttempts.Should().Be(1, "one validation, however many validators");
        }

        [Fact]
        public async Task A_throwing_validator_is_a_failed_check_not_a_passed_one()
        {
            var service = new StepValidationService(new IStepValidator[] { new Throwing(new InvalidOperationException("broke")) });

            var result = await service.ValidateStepAsync(new ImplementationStep(), _dir);

            result.Success.Should().BeFalse();
            result.AllIssues.Should().Equal("[Throwing] Validation error: broke");
        }

        [Fact]
        public async Task Cancellation_propagates_out_of_the_service()
        {
            var service = new StepValidationService(new IStepValidator[] { new Throwing(new OperationCanceledException()) });

            var act = () => service.ValidateStepAsync(new ImplementationStep(), _dir);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task Validators_can_be_added_and_removed_by_name()
        {
            var service = new StepValidationService();
            service.RemoveValidator("FileModification").Should().BeTrue();
            service.RemoveValidator("FileModification").Should().BeFalse();
            service.AddValidator(new Throwing(new Exception("x")));

            service.Validators.Select(v => v.Name).Should().Equal("FileCreation", "ContentExpectation", "Throwing");
            (await service.ValidateStepAsync(new ImplementationStep(), _dir)).Success.Should().BeFalse();
        }

        private sealed class Throwing : IStepValidator
        {
            private readonly Exception _ex;
            public Throwing(Exception ex) => _ex = ex;
            public string Name => "Throwing";
            public Task<StepValidationResult> ValidateAsync(ImplementationStep step, string workingDirectory, CancellationToken cancellationToken = default)
                => throw _ex;
        }
    }
}
