using Birko.Data.Patterns.IndexManagement;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Data.SQL.Tests.IndexManagement
{
    /// <summary>
    /// Tests for the shared IndexManagement models in Birko.Data.Patterns.
    /// Placed in SQL.Tests since it already references Birko.Data.Patterns.
    /// </summary>
    public class IndexDefinitionTests
    {
        [Fact]
        public void IndexDefinition_DefaultValues_AreCorrect()
        {
            var def = new IndexDefinition();

            def.Name.Should().BeNull();
            def.Fields.Should().BeEmpty();
            def.Unique.Should().BeFalse();
            def.Sparse.Should().BeFalse();
            def.ExpireAfter.Should().BeNull();
            def.Properties.Should().BeNull();
        }

        [Fact]
        public void IndexDefinition_SetProperties_ReturnsSetValues()
        {
            var def = new IndexDefinition
            {
                Name = "idx_user_email",
                Fields = new[] { IndexField.Ascending("Email") },
                Unique = true,
                Sparse = true,
                ExpireAfter = TimeSpan.FromDays(30),
                Properties = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["NumberOfShards"] = 3
                }
            };

            def.Name.Should().Be("idx_user_email");
            def.Fields.Should().HaveCount(1);
            def.Fields[0].Name.Should().Be("Email");
            def.Unique.Should().BeTrue();
            def.Sparse.Should().BeTrue();
            def.ExpireAfter.Should().Be(TimeSpan.FromDays(30));
            def.Properties.Should().ContainKey("NumberOfShards");
        }
    }

    public class IndexFieldTests
    {
        [Fact]
        public void Ascending_CreatesAscendingField()
        {
            var field = IndexField.Ascending("Name");

            field.Name.Should().Be("Name");
            field.IsDescending.Should().BeFalse();
            field.FieldType.Should().Be(IndexFieldType.Standard);
        }

        [Fact]
        public void Descending_CreatesDescendingField()
        {
            var field = IndexField.Descending("CreatedAt");

            field.Name.Should().Be("CreatedAt");
            field.IsDescending.Should().BeTrue();
        }

        [Fact]
        public void Text_CreatesTextField()
        {
            var field = IndexField.Text("Description");

            field.Name.Should().Be("Description");
            field.FieldType.Should().Be(IndexFieldType.Text);
        }

        [Fact]
        public void Hashed_CreatesHashedField()
        {
            var field = IndexField.Hashed("TenantId");

            field.Name.Should().Be("TenantId");
            field.FieldType.Should().Be(IndexFieldType.Hashed);
        }

        [Fact]
        public void Geo2dSphere_CreatesGeoField()
        {
            var field = IndexField.Geo2dSphere("Location");

            field.Name.Should().Be("Location");
            field.FieldType.Should().Be(IndexFieldType.Geo2dSphere);
        }

        [Fact]
        public void Default_IsAscending()
        {
            var field = new IndexField { Name = "test" };
            field.IsDescending.Should().BeFalse();
            field.FieldType.Should().Be(IndexFieldType.Standard);
        }
    }

    public class IndexInfoTests
    {
        [Fact]
        public void IndexInfo_DefaultValues_AreCorrect()
        {
            var info = new IndexInfo();

            info.Name.Should().BeNull();
            info.Fields.Should().BeEmpty();
            info.Unique.Should().BeFalse();
            info.Sparse.Should().BeFalse();
            info.ExpireAfter.Should().BeNull();
            info.SizeInBytes.Should().Be(-1);
            info.State.Should().Be("ready");
            info.Properties.Should().BeEmpty();
        }

        [Fact]
        public void IndexInfo_SetProperties_ReturnsSetValues()
        {
            var info = new IndexInfo
            {
                Name = "idx_test",
                Unique = true,
                Sparse = true,
                ExpireAfter = TimeSpan.FromHours(1),
                SizeInBytes = 12345,
                State = "stale",
                Fields = new[] { IndexField.Ascending("col1"), IndexField.Descending("col2") }
            };

            info.Name.Should().Be("idx_test");
            info.Unique.Should().BeTrue();
            info.Sparse.Should().BeTrue();
            info.ExpireAfter.Should().Be(TimeSpan.FromHours(1));
            info.SizeInBytes.Should().Be(12345);
            info.State.Should().Be("stale");
            info.Fields.Should().HaveCount(2);
        }
    }

    public class IndexManagementExceptionTests
    {
        [Fact]
        public void Constructor_Message_SetsMessage()
        {
            var ex = new IndexManagementException("test error");
            ex.Message.Should().Be("test error");
            ex.IndexName.Should().BeNull();
            ex.Scope.Should().BeNull();
        }

        [Fact]
        public void Constructor_WithIndexAndScope_SetsAll()
        {
            var ex = new IndexManagementException("test error", "idx_test", "MyTable");
            ex.Message.Should().Be("test error");
            ex.IndexName.Should().Be("idx_test");
            ex.Scope.Should().Be("MyTable");
        }

        [Fact]
        public void Constructor_WithInnerException_SetsAll()
        {
            var inner = new InvalidOperationException("inner");
            var ex = new IndexManagementException("test error", "idx_test", "MyTable", inner);
            ex.Message.Should().Be("test error");
            ex.InnerException.Should().BeSameAs(inner);
            ex.IndexName.Should().Be("idx_test");
            ex.Scope.Should().Be("MyTable");
        }
    }
}
