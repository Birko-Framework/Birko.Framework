using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Validation.Fluent;
using Birko.Validation.Integration;
using Birko.Validation.Rules;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Validation.Tests.Integration;

/// <summary>
/// TASK-156, the validation-and-rules medium findings on Birko.Validation, measured on a validator's result and on what an
/// in-memory store holds.
/// </summary>
public class ValidationTriageTests
{
    public class Address
    {
        public string? City { get; set; }
    }

    public class Device : AbstractModel
    {
        public string Name { get; set; } = "d";
        public double Temperature { get; set; }
        public decimal Price { get; set; }
        public string? Status { get; set; }
        public Address? Address { get; set; }
        public Address? Billing { get; set; }
        public int Count { get; set; }
    }

    private sealed class RangeValidator : AbstractValidator<Device>
    {
        public RangeValidator()
        {
            // The shape AbstractValidator's own doc example teaches: int bounds on a double property.
            RuleFor(x => x.Temperature).Range(-50, 150);
            RuleFor(x => x.Price).Range(0, 10);
        }
    }

    private sealed class NestedValidator : AbstractValidator<Device>
    {
        public NestedValidator()
        {
            RuleFor(x => x.Address!.City).Required();
            RuleFor(x => x.Billing!.City).Required();
        }
    }

    private sealed class StatusValidator : AbstractValidator<Device>
    {
        public StatusValidator()
        {
            RuleFor(x => x.Status).In(new[] { "A", "B" }, "Pick A or B.");
        }
    }

    private sealed class CountValidator : AbstractValidator<Device>
    {
        public CountValidator()
        {
            RuleFor(x => x.Count).LessThanOrEqual(5);
        }
    }

    [Fact]
    public void SH_M371_int_bounds_on_a_double_property_report_out_of_range_instead_of_throwing()
    {
        var validator = new RangeValidator();

        validator.Validate(new Device { Temperature = 20.5, Price = 5m }).IsValid.Should().BeTrue();
        var result = validator.Validate(new Device { Temperature = 151.5, Price = 10.01m });

        result.Errors.Select(e => (e.PropertyName, e.ErrorCode)).Should().BeEquivalentTo(new[]
        {
            ("Temperature", "OUT_OF_RANGE"),
            ("Price", "OUT_OF_RANGE"),
        });
    }

    [Fact]
    public void SH_M371_a_bound_that_cannot_compare_with_the_value_names_the_property()
    {
        var rule = new RangeRule("Name", min: 5);

        var act = () => rule.IsValid("text", new ValidationContext(new object()));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Name*");
    }

    [Fact]
    public void SH_M372_a_null_parent_is_a_missing_value_not_a_crash_and_keys_carry_the_whole_path()
    {
        var result = new NestedValidator().Validate(new Device { Address = null, Billing = new Address { City = null } });

        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(new[] { "Address.City", "Billing.City" });
    }

    [Fact]
    public void SH_M375_In_lets_null_through_and_takes_a_message()
    {
        var validator = new StatusValidator();

        validator.Validate(new Device { Status = null }).IsValid.Should().BeTrue();
        validator.Validate(new Device { Status = "C" }).Errors.Should().ContainSingle().Which.Message.Should().Be("Pick A or B.");
    }

    [Fact]
    public void SH_M376_a_backtracking_pattern_times_out_as_invalid_instead_of_spinning()
    {
        var rule = new RegexRule("Name", "^(a+)+$", matchTimeout: TimeSpan.FromMilliseconds(50));

        var started = DateTime.UtcNow;
        var valid = rule.IsValid(new string('a', 40) + "!", new ValidationContext(new object()));

        valid.Should().BeFalse();
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SH_M376_the_default_options_are_not_compiled_and_carry_a_timeout()
    {
        var regex = (System.Text.RegularExpressions.Regex)typeof(RegexRule)
            .GetField("_regex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(new RegexRule("Name", "^x$"))!;

        regex.Options.HasFlag(System.Text.RegularExpressions.RegexOptions.Compiled).Should().BeFalse();
        regex.MatchTimeout.Should().Be(RegexRule.DefaultMatchTimeout);
    }

    [Fact]
    public async Task SH_M373_a_filtered_Action_update_is_validated()
    {
        var store = new AsyncInMemoryStore<Device>();
        await store.CreateAsync(new Device { Name = "a", Count = 1 });
        var wrapper = new AsyncValidatingBulkStoreWrapper<AsyncInMemoryStore<Device>, Device>(store, new CountValidator());

        var act = () => wrapper.UpdateAsync(x => x.Name == "a", d => d.Count = 9);

        await act.Should().ThrowAsync<ValidationException>();
        (await store.ReadAsync(x => x.Name == "a", null, null, null)).Single().Count.Should().Be(1);
    }

    [Fact]
    public async Task SH_M373_a_filtered_PropertyUpdate_is_validated_and_a_valid_one_runs()
    {
        var store = new AsyncInMemoryStore<Device>();
        await store.CreateAsync(new Device { Name = "a", Count = 1 });
        var wrapper = new AsyncValidatingBulkStoreWrapper<AsyncInMemoryStore<Device>, Device>(store, new CountValidator());

        var invalid = () => wrapper.UpdateAsync(x => x.Name == "a", new PropertyUpdate<Device>().Increment(x => x.Count, 10));
        await invalid.Should().ThrowAsync<ValidationException>();
        await wrapper.UpdateAsync(x => x.Name == "a", new PropertyUpdate<Device>().Increment(x => x.Count, 2));

        (await store.ReadAsync(x => x.Name == "a", null, null, null)).Single().Count.Should().Be(3);
    }

    [Fact]
    public async Task SH_M374_a_single_pass_batch_is_validated_and_written_once()
    {
        var store = new AsyncInMemoryStore<Device>();
        var wrapper = new AsyncValidatingBulkStoreWrapper<AsyncInMemoryStore<Device>, Device>(store, new CountValidator());
        var enumerations = 0;
        IEnumerable<Device> Batch()
        {
            enumerations++;
            yield return new Device { Name = "x", Count = 1 };
            yield return new Device { Name = "y", Count = 2 };
        }

        await wrapper.CreateAsync(Batch());

        enumerations.Should().Be(1);
        (await store.CountAsync(null)).Should().Be(2);
    }
}
