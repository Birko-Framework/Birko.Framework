using System;
using Birko.Helpers;
using FluentAssertions;
using Xunit;

namespace Birko.Helpers.Tests
{
    public class GuidHelperTests
    {
        [Fact]
        public void Normalize_maps_null_to_null()
        {
            GuidHelper.Normalize(null).Should().BeNull();
        }

        [Fact]
        public void Normalize_maps_empty_guid_to_null()
        {
            GuidHelper.Normalize(Guid.Empty).Should().BeNull();
        }

        [Fact]
        public void Normalize_returns_real_guid_unchanged()
        {
            var id = Guid.NewGuid();
            GuidHelper.Normalize(id).Should().Be(id);
        }
    }
}
