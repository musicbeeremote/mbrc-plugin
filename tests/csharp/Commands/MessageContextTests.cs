using System;
using FluentAssertions;
using MusicBeePlugin.Commands.Infrastructure;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Commands
{
    public class MessageContextTests
    {
        private const string TestConnectionId = "test-connection-123";

        #region Constructor and Properties

        [Fact]
        public void Constructor_SetsProperties()
        {
            // Arrange & Act
            var context = new MessageContext("test-command", "test-data", TestConnectionId);

            // Assert
            context.CommandType.Should().Be("test-command");
            context.Data.Should().Be("test-data");
            context.ConnectionId.Should().Be(TestConnectionId);
        }

        [Fact]
        public void Constructor_AcceptsNullData()
        {
            // Arrange & Act
            var context = new MessageContext("test-command", null, TestConnectionId);

            // Assert
            context.Data.Should().BeNull();
        }

        [Fact]
        public void ToString_ReturnsFormattedString()
        {
            // Arrange
            var context = new MessageContext("player", "data", TestConnectionId);

            // Act
            var result = context.ToString();

            // Assert
            result.Should().Contain("player");
            result.Should().Contain(TestConnectionId);
        }

        #endregion

        #region TryGetData - Direct Type Match

        [Fact]
        public void TryGetData_DirectStringMatch_ReturnsTrue()
        {
            // Arrange
            var context = new MessageContext("test", "hello world", TestConnectionId);

            // Act
            var result = context.TryGetData<string>(out var value);

            // Assert
            result.Should().BeTrue();
            value.Should().Be("hello world");
        }

        [Fact]
        public void TryGetData_DirectIntMatch_ReturnsTrue()
        {
            // Arrange
            var context = new MessageContext("test", 42, TestConnectionId);

            // Act
            var result = context.TryGetData<int>(out var value);

            // Assert
            result.Should().BeTrue();
            value.Should().Be(42);
        }

        [Fact]
        public void TryGetData_DirectBoolMatch_ReturnsTrue()
        {
            // Arrange
            var context = new MessageContext("test", true, TestConnectionId);

            // Act
            var result = context.TryGetData<bool>(out var value);

            // Assert
            result.Should().BeTrue();
            value.Should().BeTrue();
        }

        [Fact]
        public void TryGetData_DirectObjectMatch_ReturnsTrue()
        {
            // Arrange
            var testObject = new TestDataClass { Name = "Test", Value = 123 };
            var context = new MessageContext("test", testObject, TestConnectionId);

            // Act
            var result = context.TryGetData<TestDataClass>(out var value);

            // Assert
            result.Should().BeTrue();
            value.Name.Should().Be("Test");
            value.Value.Should().Be(123);
        }

        #endregion

        #region TryGetData - JToken Conversion

        [Fact]
        public void TryGetData_JObjectToComplexType_ReturnsTrue()
        {
            // Arrange
            var jObject = JObject.FromObject(new { Name = "Test", Value = 456 });
            var context = new MessageContext("test", jObject, TestConnectionId);

            // Act
            var result = context.TryGetData<TestDataClass>(out var value);

            // Assert
            result.Should().BeTrue();
            value.Name.Should().Be("Test");
            value.Value.Should().Be(456);
        }

        [Fact]
        public void TryGetData_JValueToInt_ReturnsTrue()
        {
            // Arrange
            var jValue = new JValue(100);
            var context = new MessageContext("test", jValue, TestConnectionId);

            // Act
            var result = context.TryGetData<int>(out var value);

            // Assert
            result.Should().BeTrue();
            value.Should().Be(100);
        }

        [Fact]
        public void TryGetData_JValueToString_ReturnsTrue()
        {
            // Arrange
            var jValue = new JValue("test string");
            var context = new MessageContext("test", jValue, TestConnectionId);

            // Act
            var result = context.TryGetData<string>(out var value);

            // Assert
            result.Should().BeTrue();
            value.Should().Be("test string");
        }

        [Fact]
        public void TryGetData_JValueToBool_ReturnsTrue()
        {
            // Arrange
            var jValue = new JValue(true);
            var context = new MessageContext("test", jValue, TestConnectionId);

            // Act
            var result = context.TryGetData<bool>(out var value);

            // Assert
            result.Should().BeTrue();
            value.Should().BeTrue();
        }

        [Fact]
        public void TryGetData_JArrayToList_ReturnsTrue()
        {
            // Arrange
            var jArray = JArray.FromObject(new[] { "a", "b", "c" });
            var context = new MessageContext("test", jArray, TestConnectionId);

            // Act
            var result = context.TryGetData<string[]>(out var value);

            // Assert
            result.Should().BeTrue();
            value.Should().HaveCount(3);
            value.Should().Contain("a", "b", "c");
        }

        #endregion

        #region TryGetData - String Parsing

        [Fact]
        public void TryGetData_StringToInt_ReturnsTrue()
        {
            // Arrange
            var context = new MessageContext("test", "42", TestConnectionId);

            // Act
            var result = context.TryGetData<int>(out var value);

            // Assert
            result.Should().BeTrue();
            value.Should().Be(42);
        }

        [Fact]
        public void TryGetData_StringToInt_InvalidFormat_ReturnsFalse()
        {
            // Arrange
            var context = new MessageContext("test", "not-a-number", TestConnectionId);

            // Act
            var result = context.TryGetData<int>(out var value);

            // Assert
            result.Should().BeFalse();
            value.Should().Be(default);
        }

        [Fact]
        public void TryGetData_StringToInt_EmptyString_ReturnsFalse()
        {
            // Arrange
            var context = new MessageContext("test", "", TestConnectionId);

            // Act
            var result = context.TryGetData<int>(out var value);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void TryGetData_StringToBool_True_ReturnsTrue()
        {
            // Arrange
            var context = new MessageContext("test", "true", TestConnectionId);

            // Act
            var result = context.TryGetData<bool>(out var value);

            // Assert
            result.Should().BeTrue();
            value.Should().BeTrue();
        }

        [Fact]
        public void TryGetData_StringToBool_False_ReturnsTrue()
        {
            // Arrange
            var context = new MessageContext("test", "false", TestConnectionId);

            // Act
            var result = context.TryGetData<bool>(out var value);

            // Assert
            result.Should().BeTrue();
            value.Should().BeFalse();
        }

        [Fact]
        public void TryGetData_StringToBool_CaseInsensitive()
        {
            // Arrange
            var context = new MessageContext("test", "TRUE", TestConnectionId);

            // Act
            var result = context.TryGetData<bool>(out var value);

            // Assert
            result.Should().BeTrue();
            value.Should().BeTrue();
        }

        [Fact]
        public void TryGetData_StringToBool_InvalidFormat_ReturnsFalse()
        {
            // Arrange
            var context = new MessageContext("test", "yes", TestConnectionId);

            // Act
            var result = context.TryGetData<bool>(out var value);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void TryGetData_StringToEnum_ValidValue_ReturnsTrue()
        {
            // Arrange
            var context = new MessageContext("test", "Playing", TestConnectionId);

            // Act
            var result = context.TryGetData<TestPlayState>(out var value);

            // Assert
            result.Should().BeTrue();
            value.Should().Be(TestPlayState.Playing);
        }

        [Fact]
        public void TryGetData_StringToEnum_CaseInsensitive()
        {
            // Arrange
            var context = new MessageContext("test", "playing", TestConnectionId);

            // Act
            var result = context.TryGetData<TestPlayState>(out var value);

            // Assert
            result.Should().BeTrue();
            value.Should().Be(TestPlayState.Playing);
        }

        [Fact]
        public void TryGetData_StringToEnum_InvalidValue_ReturnsFalse()
        {
            // Arrange
            var context = new MessageContext("test", "InvalidState", TestConnectionId);

            // Act
            var result = context.TryGetData<TestPlayState>(out var value);

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region TryGetData - Null and Edge Cases

        [Fact]
        public void TryGetData_NullData_ReturnsFalse()
        {
            // Arrange
            var context = new MessageContext("test", null, TestConnectionId);

            // Act
            var result = context.TryGetData<string>(out var value);

            // Assert
            result.Should().BeFalse();
            value.Should().BeNull();
        }

        [Fact]
        public void TryGetData_TypeMismatch_ReturnsFalse()
        {
            // Arrange - data is string, trying to get int directly (not parse)
            var context = new MessageContext("test", new object(), TestConnectionId);

            // Act
            var result = context.TryGetData<int>(out var value);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void TryGetData_NegativeInt_Works()
        {
            // Arrange
            var context = new MessageContext("test", "-100", TestConnectionId);

            // Act
            var result = context.TryGetData<int>(out var value);

            // Assert
            result.Should().BeTrue();
            value.Should().Be(-100);
        }

        #endregion

        #region GetDataOrDefault

        [Fact]
        public void GetDataOrDefault_ValidData_ReturnsValue()
        {
            // Arrange
            var context = new MessageContext("test", "42", TestConnectionId);

            // Act
            var result = context.GetDataOrDefault<int>();

            // Assert
            result.Should().Be(42);
        }

        [Fact]
        public void GetDataOrDefault_InvalidData_ReturnsDefault()
        {
            // Arrange
            var context = new MessageContext("test", "invalid", TestConnectionId);

            // Act
            var result = context.GetDataOrDefault<int>();

            // Assert
            result.Should().Be(0);
        }

        [Fact]
        public void GetDataOrDefault_InvalidData_ReturnsCustomDefault()
        {
            // Arrange
            var context = new MessageContext("test", "invalid", TestConnectionId);

            // Act
            var result = context.GetDataOrDefault(-1);

            // Assert
            result.Should().Be(-1);
        }

        [Fact]
        public void GetDataOrDefault_NullData_ReturnsDefault()
        {
            // Arrange
            var context = new MessageContext("test", null, TestConnectionId);

            // Act
            var result = context.GetDataOrDefault("default-value");

            // Assert
            result.Should().Be("default-value");
        }

        #endregion

        #region SafeGetValue Static Method

        [Fact]
        public void SafeGetValue_NullToken_ReturnsDefault()
        {
            // Act
            var result = MessageContext.SafeGetValue<int>(null);

            // Assert
            result.Should().Be(0);
        }

        [Fact]
        public void SafeGetValue_NullToken_ReturnsCustomDefault()
        {
            // Act
            var result = MessageContext.SafeGetValue<int>(null, 42);

            // Assert
            result.Should().Be(42);
        }

        [Fact]
        public void SafeGetValue_ValidIntToken_ReturnsValue()
        {
            // Arrange
            var token = new JValue(100);

            // Act
            var result = MessageContext.SafeGetValue<int>(token);

            // Assert
            result.Should().Be(100);
        }

        [Fact]
        public void SafeGetValue_ValidStringToken_ReturnsValue()
        {
            // Arrange
            var token = new JValue("hello");

            // Act
            var result = MessageContext.SafeGetValue<string>(token);

            // Assert
            result.Should().Be("hello");
        }

        [Fact]
        public void SafeGetValue_StringToInt_ReturnsValue()
        {
            // Arrange
            var token = new JValue("123");

            // Act
            var result = MessageContext.SafeGetValue<int>(token);

            // Assert
            result.Should().Be(123);
        }

        [Fact]
        public void SafeGetValue_StringToBool_ReturnsValue()
        {
            // Arrange
            var token = new JValue("true");

            // Act
            var result = MessageContext.SafeGetValue<bool>(token);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void SafeGetValue_StringToEnum_ReturnsValue()
        {
            // Arrange
            var token = new JValue("Paused");

            // Act
            var result = MessageContext.SafeGetValue<TestPlayState>(token);

            // Assert
            result.Should().Be(TestPlayState.Paused);
        }

        [Fact]
        public void SafeGetValue_InvalidEnumValue_ReturnsDefault()
        {
            // Arrange
            var token = new JValue("Invalid");

            // Act
            var result = MessageContext.SafeGetValue<TestPlayState>(token);

            // Assert
            result.Should().Be(default(TestPlayState));
        }

        [Fact]
        public void SafeGetValue_EmptyString_ReturnsDefault()
        {
            // Arrange
            var token = new JValue("");

            // Act
            var result = MessageContext.SafeGetValue<int>(token, -1);

            // Assert
            result.Should().Be(-1);
        }

        [Fact]
        public void SafeGetValue_JValueString_ReturnsString()
        {
            // Arrange
            var token = new JValue("test value");

            // Act
            var result = MessageContext.SafeGetValue<string>(token);

            // Assert
            result.Should().Be("test value");
        }

        #endregion

        #region Test Helpers

        private sealed class TestDataClass
        {
            public string Name { get; set; }
            public int Value { get; set; }
        }

        private enum TestPlayState
        {
            Stopped,
            Playing,
            Paused
        }

        #endregion
    }
}
