using System.Data;
using System.Data.Common;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Tests.TestHelpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace Birko.Data.SQL.Tests.Connectors
{
    public class SqlBuilderContextTests
    {
        private readonly Mock<AbstractConnector> _connectorMock;
        private readonly TestDbCommand _command;

        public SqlBuilderContextTests()
        {
            _connectorMock = new Mock<AbstractConnector>(MockBehavior.Strict, new Birko.Configuration.PasswordSettings());
            _command = new TestDbCommand();

            _connectorMock
                .Setup(c => c.AddParameter(It.IsAny<DbCommand>(), It.IsAny<string>(), It.IsAny<object>()))
                .Returns(_command);
        }

        [Fact]
        public void Constructor_ShouldThrow_WhenConnectorIsNull()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SqlBuilderContext(null!));
        }

        [Fact]
        public void GenerateParameterName_ShouldReturnValidParameterName()
        {
            // Arrange
            var context = new SqlBuilderContext(_connectorMock.Object);

            // Act
            var paramName = context.GenerateParameterName("FieldName", 0, _command);

            // Assert
            paramName.Should().Be("@WHEREFieldName0_0");
        }

        [Fact]
        public void GenerateParameterName_ShouldRemoveDotsFromFieldName()
        {
            // Arrange
            var context = new SqlBuilderContext(_connectorMock.Object);

            // Act
            var paramName = context.GenerateParameterName("Table.FieldName", 0, _command);

            // Assert
            paramName.Should().Be("@WHERETableFieldName0_0");
        }

        [Fact]
        public void GenerateParameterName_ShouldIncludeIndex()
        {
            // Arrange
            var context = new SqlBuilderContext(_connectorMock.Object);

            // Act
            var param1 = context.GenerateParameterName("Field", 0, _command);
            var param2 = context.GenerateParameterName("Field", 1, _command);
            var param3 = context.GenerateParameterName("Field", 2, _command);

            // Assert
            param1.Should().Be("@WHEREField0_0");
            param2.Should().Be("@WHEREField1_0");
            param3.Should().Be("@WHEREField2_0");
        }

        [Fact]
        public void GenerateParameterName_ShouldIncludeParameterCount()
        {
            // Arrange
            // Add 5 dummy parameters to the command
            for (int i = 0; i < 5; i++)
            {
                _command.Parameters.Add(new TestDbParameter { ParameterName = $"@Param{i}" });
            }
            var context = new SqlBuilderContext(_connectorMock.Object);

            // Act
            var paramName = context.GenerateParameterName("Field", 0, _command);

            // Assert
            paramName.Should().Be("@WHEREField0_5");
        }

        [Fact]
        public void GenerateParameterName_ShouldThrow_WhenFieldNameIsNull()
        {
            // Arrange
            var context = new SqlBuilderContext(_connectorMock.Object);

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                context.GenerateParameterName(null!, 0, _command));
        }

        [Fact]
        public void GenerateParameterName_ShouldThrow_WhenFieldNameIsEmpty()
        {
            // Arrange
            var context = new SqlBuilderContext(_connectorMock.Object);

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                context.GenerateParameterName(string.Empty, 0, _command));
        }

        [Fact]
        public void FormatValue_ForStartsWith_ShouldAppendWildcard()
        {
            // Arrange
            var context = new SqlBuilderContext(_connectorMock.Object);

            // Act
            var result = context.FormatValue("test", ConditionType.StartsWith);

            // Assert
            result.Should().Be("test%");
        }

        [Fact]
        public void FormatValue_ForLike_ShouldWrapWithWildcards()
        {
            // Arrange
            var context = new SqlBuilderContext(_connectorMock.Object);

            // Act
            var result = context.FormatValue("test", ConditionType.Like);

            // Assert
            result.Should().Be("%test%");
        }

        [Fact]
        public void FormatValue_ForEndsWith_ShouldPrependWildcard()
        {
            // Arrange
            var context = new SqlBuilderContext(_connectorMock.Object);

            // Act
            var result = context.FormatValue("test", ConditionType.EndsWith);

            // Assert
            result.Should().Be("%test");
        }

        [Fact]
        public void FormatValue_ForNonLikeConditions_ShouldReturnValueAsIs()
        {
            // Arrange
            var context = new SqlBuilderContext(_connectorMock.Object);

            // Act
            var result = context.FormatValue("test", ConditionType.Equal);

            // Assert
            result.Should().Be("test");
        }

        [Fact]
        public void FormatValue_ForNonStringValue_ShouldReturnValueAsIs()
        {
            // Arrange
            var context = new SqlBuilderContext(_connectorMock.Object);
            var intValue = 123;

            // Act
            var result = context.FormatValue(intValue, ConditionType.Like);

            // Assert
            result.Should().Be(123);
        }

        [Fact]
        public void EscapeValue_ForString_ShouldEscapeSingleQuotes()
        {
            // Arrange
            var context = new SqlBuilderContext(_connectorMock.Object);

            // Act
            var result = context.EscapeValue("O'Reilly");

            // Assert
            result.Should().Be("O''Reilly");
        }

        [Fact]
        public void EscapeValue_ForMultipleQuotes_ShouldEscapeAll()
        {
            // Arrange
            var context = new SqlBuilderContext(_connectorMock.Object);

            // Act
            var result = context.EscapeValue("It's a 'test' string");

            // Assert
            result.Should().Be("It''s a ''test'' string");
        }

        [Fact]
        public void EscapeValue_ForNonString_ShouldReturnValueAsIs()
        {
            // Arrange
            var context = new SqlBuilderContext(_connectorMock.Object);

            // Act
            var result = context.EscapeValue(12345);

            // Assert
            result.Should().Be(12345);
        }

        [Fact]
        public void AddParameter_ShouldCallConnectorAddParameter()
        {
            // Arrange
            var context = new SqlBuilderContext(_connectorMock.Object);
            var paramName = "@TestParam";
            var value = "testValue";

            // Act
            context.AddParameter(_command, paramName, value);

            // Assert
            _connectorMock.Verify(
                c => c.AddParameter(_command, paramName, value),
                Times.Once);
        }

        [Fact]
        public void FormatValue_ShouldHandleEmptyString()
        {
            // Arrange
            var context = new SqlBuilderContext(_connectorMock.Object);

            // Act
            var result = context.FormatValue(string.Empty, ConditionType.StartsWith);

            // Assert
            result.Should().Be("%");
        }

        [Fact]
        public void FormatValue_ShouldHandleStringWithWildcards()
        {
            // Arrange
            var context = new SqlBuilderContext(_connectorMock.Object);
            var input = "test%value";

            // Act
            var result = context.FormatValue(input, ConditionType.Like);

            // Assert
            result.Should().Be("%test%value%");
        }

        [Fact]
        public void EscapeValue_ShouldHandleNull()
        {
            // Arrange
            var context = new SqlBuilderContext(_connectorMock.Object);

            // Act
            var result = context.EscapeValue(null);

            // Assert
            result.Should().BeNull();
        }
    }
}
