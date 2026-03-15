using Birko.Telemetry;
using FluentAssertions;
using Xunit;

namespace Birko.Telemetry.Tests;

public class ConventionsTests
{
    [Theory]
    [InlineData(nameof(BirkoTelemetryConventions.MeterName))]
    [InlineData(nameof(BirkoTelemetryConventions.ActivitySourceName))]
    [InlineData(nameof(BirkoTelemetryConventions.OperationDurationMetric))]
    [InlineData(nameof(BirkoTelemetryConventions.OperationCountMetric))]
    [InlineData(nameof(BirkoTelemetryConventions.OperationErrorMetric))]
    [InlineData(nameof(BirkoTelemetryConventions.StoreTypeTag))]
    [InlineData(nameof(BirkoTelemetryConventions.EntityTypeTag))]
    [InlineData(nameof(BirkoTelemetryConventions.OperationTag))]
    [InlineData(nameof(BirkoTelemetryConventions.TenantTag))]
    [InlineData(nameof(BirkoTelemetryConventions.BulkTag))]
    [InlineData(nameof(BirkoTelemetryConventions.DefaultCorrelationIdHeader))]
    public void AllConstants_ShouldBeNonNullAndNonEmpty(string fieldName)
    {
        var field = typeof(BirkoTelemetryConventions).GetField(fieldName);
        field.Should().NotBeNull();

        var value = field!.GetValue(null) as string;
        value.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void MeterName_ShouldBeBirkoDataStore()
    {
        BirkoTelemetryConventions.MeterName.Should().Be("Birko.Data.Store");
    }

    [Fact]
    public void DefaultCorrelationIdHeader_ShouldBeXCorrelationId()
    {
        BirkoTelemetryConventions.DefaultCorrelationIdHeader.Should().Be("X-Correlation-Id");
    }
}
