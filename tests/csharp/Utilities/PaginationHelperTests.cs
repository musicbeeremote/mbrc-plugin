using FluentAssertions;
using MusicBeePlugin.Utilities.Data;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Utilities
{
    public class PaginationHelperTests
    {
        private const int DefaultLimit = 4000;

        #region JToken Extension Method Tests

        [Fact]
        public void ParsePagination_JToken_ReturnsDefaults_WhenDataIsNull()
        {
            // Arrange
            JToken data = null;

            // Act
            var (offset, limit) = data.ParsePagination();

            // Assert
            offset.Should().Be(0);
            limit.Should().Be(DefaultLimit);
        }

        [Fact]
        public void ParsePagination_JToken_ReturnsDefaults_WhenDataIsEmpty()
        {
            // Arrange
            var data = JToken.Parse("{}");

            // Act
            var (offset, limit) = data.ParsePagination();

            // Assert
            offset.Should().Be(0);
            limit.Should().Be(DefaultLimit);
        }

        [Fact]
        public void ParsePagination_JToken_ParsesOffsetCorrectly()
        {
            // Arrange
            var data = JToken.Parse("{\"offset\": 10}");

            // Act
            var (offset, limit) = data.ParsePagination();

            // Assert
            offset.Should().Be(10);
            limit.Should().Be(DefaultLimit);
        }

        [Fact]
        public void ParsePagination_JToken_ParsesLimitCorrectly()
        {
            // Arrange
            var data = JToken.Parse("{\"limit\": 50}");

            // Act
            var (offset, limit) = data.ParsePagination();

            // Assert
            offset.Should().Be(0);
            limit.Should().Be(50);
        }

        [Fact]
        public void ParsePagination_JToken_ParsesBothOffsetAndLimit()
        {
            // Arrange
            var data = JToken.Parse("{\"offset\": 20, \"limit\": 100}");

            // Act
            var (offset, limit) = data.ParsePagination();

            // Assert
            offset.Should().Be(20);
            limit.Should().Be(100);
        }

        [Fact]
        public void ParsePagination_JToken_UsesCustomDefaultLimit()
        {
            // Arrange
            JToken data = null;
            const int customDefault = 500;

            // Act
            var (offset, limit) = data.ParsePagination(customDefault);

            // Assert
            offset.Should().Be(0);
            limit.Should().Be(customDefault);
        }

        [Fact]
        public void ParsePagination_JToken_IgnoresUnknownProperties()
        {
            // Arrange
            var data = JToken.Parse("{\"offset\": 5, \"limit\": 25, \"other\": \"value\"}");

            // Act
            var (offset, limit) = data.ParsePagination();

            // Assert
            offset.Should().Be(5);
            limit.Should().Be(25);
        }

        [Fact]
        public void ParsePagination_JToken_HandlesZeroValues()
        {
            // Arrange
            var data = JToken.Parse("{\"offset\": 0, \"limit\": 0}");

            // Act
            var (offset, limit) = data.ParsePagination();

            // Assert
            offset.Should().Be(0);
            limit.Should().Be(0);
        }

        [Fact]
        public void ParsePagination_JToken_HandlesLargeValues()
        {
            // Arrange
            var data = JToken.Parse("{\"offset\": 1000000, \"limit\": 500000}");

            // Act
            var (offset, limit) = data.ParsePagination();

            // Assert
            offset.Should().Be(1000000);
            limit.Should().Be(500000);
        }

        [Theory]
        [InlineData(0, 10)]
        [InlineData(10, 20)]
        [InlineData(100, 50)]
        [InlineData(1000, 100)]
        public void ParsePagination_JToken_WorksWithVariousValues(int expectedOffset, int expectedLimit)
        {
            // Arrange
            var data = JToken.Parse($"{{\"offset\": {expectedOffset}, \"limit\": {expectedLimit}}}");

            // Act
            var (offset, limit) = data.ParsePagination();

            // Assert
            offset.Should().Be(expectedOffset);
            limit.Should().Be(expectedLimit);
        }

        #endregion

        #region Object Overload Tests

        [Fact]
        public void ParsePagination_Object_ReturnsDefaults_WhenEventDataIsNull()
        {
            // Act
            var (offset, limit) = PaginationHelper.ParsePagination(null);

            // Assert
            offset.Should().Be(0);
            limit.Should().Be(DefaultLimit);
        }

        [Fact]
        public void ParsePagination_Object_ReturnsDefaults_WhenEventDataIsNotJToken()
        {
            // Arrange
            var eventData = "not a JToken";

            // Act
            var (offset, limit) = PaginationHelper.ParsePagination(eventData);

            // Assert
            offset.Should().Be(0);
            limit.Should().Be(DefaultLimit);
        }

        [Fact]
        public void ParsePagination_Object_ParsesJToken()
        {
            // Arrange
            object eventData = JToken.Parse("{\"offset\": 15, \"limit\": 75}");

            // Act
            var (offset, limit) = PaginationHelper.ParsePagination(eventData);

            // Assert
            offset.Should().Be(15);
            limit.Should().Be(75);
        }

        [Fact]
        public void ParsePagination_Object_UsesCustomDefaultLimit()
        {
            // Arrange
            const int customDefault = 200;

            // Act
            var (offset, limit) = PaginationHelper.ParsePagination(null, customDefault);

            // Assert
            offset.Should().Be(0);
            limit.Should().Be(customDefault);
        }

        [Fact]
        public void ParsePagination_Object_ReturnsCustomDefault_WhenNotJToken()
        {
            // Arrange
            var eventData = new object();
            const int customDefault = 300;

            // Act
            var (offset, limit) = PaginationHelper.ParsePagination(eventData, customDefault);

            // Assert
            offset.Should().Be(0);
            limit.Should().Be(customDefault);
        }

        [Theory]
        [InlineData(typeof(int))]
        [InlineData(typeof(string))]
        [InlineData(typeof(object))]
        public void ParsePagination_Object_ReturnsDefaults_ForNonJTokenTypes(System.Type type)
        {
            // Arrange
            object eventData;
            if (type == typeof(int))
                eventData = 42;
            else if (type == typeof(string))
                eventData = "test";
            else
                eventData = new object();

            // Act
            var (offset, limit) = PaginationHelper.ParsePagination(eventData);

            // Assert
            offset.Should().Be(0);
            limit.Should().Be(DefaultLimit);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void ParsePagination_JToken_HandlesNegativeOffset()
        {
            // Arrange
            var data = JToken.Parse("{\"offset\": -5}");

            // Act
            var (offset, limit) = data.ParsePagination();

            // Assert
            offset.Should().Be(-5);
        }

        [Fact]
        public void ParsePagination_JToken_HandlesNegativeLimit()
        {
            // Arrange
            var data = JToken.Parse("{\"limit\": -10}");

            // Act
            var (offset, limit) = data.ParsePagination();

            // Assert
            limit.Should().Be(-10);
        }

        [Fact]
        public void ParsePagination_JToken_HandlesMixedCaseProperties()
        {
            // Arrange - JSON property names are case-sensitive, these should not match
            var data = JToken.Parse("{\"Offset\": 10, \"Limit\": 20}");

            // Act
            var (offset, limit) = data.ParsePagination();

            // Assert
            offset.Should().Be(0);
            limit.Should().Be(DefaultLimit);
        }

        [Fact]
        public void ParsePagination_JToken_HandlesNestedObject()
        {
            // Arrange
            var data = JToken.Parse("{\"offset\": 5, \"limit\": 10, \"nested\": {\"value\": 1}}");

            // Act
            var (offset, limit) = data.ParsePagination();

            // Assert
            offset.Should().Be(5);
            limit.Should().Be(10);
        }

        #endregion
    }
}
