using System;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Utilities.Data;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Utilities
{
    public class XmlFilterHelperTests
    {
        [Fact]
        public void CreateFilter_GeneratesValidXml()
        {
            // Arrange
            var tags = new[] { "Artist" };
            var query = "test";

            // Act
            var result = XmlFilterHelper.CreateFilter(tags, query, false, SearchSource.Library);

            // Assert
            var parsed = XElement.Parse(result);
            parsed.Should().NotBeNull();
        }

        [Fact]
        public void CreateFilter_SetsCorrectSourceType()
        {
            // Arrange
            var tags = new[] { "Artist" };
            var query = "test";

            // Act
            var result = XmlFilterHelper.CreateFilter(tags, query, false, SearchSource.Library);

            // Assert
            var parsed = XElement.Parse(result);
            parsed.Attribute("Type").Value.Should().Be("1"); // Library = 1
        }

        [Fact]
        public void CreateFilter_UsesContainsComparison_WhenNotStrict()
        {
            // Arrange
            var tags = new[] { "Artist" };
            var query = "test";

            // Act
            var result = XmlFilterHelper.CreateFilter(tags, query, isStrict: false, SearchSource.Library);

            // Assert
            var parsed = XElement.Parse(result);
            var condition = parsed.Element("Conditions").Element("Condition");
            condition.Attribute("Comparison").Value.Should().Be("Contains");
        }

        [Fact]
        public void CreateFilter_UsesIsComparison_WhenStrict()
        {
            // Arrange
            var tags = new[] { "Artist" };
            var query = "test";

            // Act
            var result = XmlFilterHelper.CreateFilter(tags, query, isStrict: true, SearchSource.Library);

            // Assert
            var parsed = XElement.Parse(result);
            var condition = parsed.Element("Conditions").Element("Condition");
            condition.Attribute("Comparison").Value.Should().Be("Is");
        }

        [Fact]
        public void CreateFilter_SetsConditionField()
        {
            // Arrange
            var tags = new[] { "Artist" };
            var query = "test";

            // Act
            var result = XmlFilterHelper.CreateFilter(tags, query, false, SearchSource.Library);

            // Assert
            var parsed = XElement.Parse(result);
            var condition = parsed.Element("Conditions").Element("Condition");
            condition.Attribute("Field").Value.Should().Be("Artist");
        }

        [Fact]
        public void CreateFilter_SetsConditionValue()
        {
            // Arrange
            var tags = new[] { "Artist" };
            var query = "Beatles";

            // Act
            var result = XmlFilterHelper.CreateFilter(tags, query, false, SearchSource.Library);

            // Assert
            var parsed = XElement.Parse(result);
            var condition = parsed.Element("Conditions").Element("Condition");
            condition.Attribute("Value").Value.Should().Be("Beatles");
        }

        [Fact]
        public void CreateFilter_CreatesMultipleConditions_ForMultipleTags()
        {
            // Arrange
            var tags = new[] { "Artist", "Album", "Title" };
            var query = "test";

            // Act
            var result = XmlFilterHelper.CreateFilter(tags, query, false, SearchSource.Library);

            // Assert
            var parsed = XElement.Parse(result);
            var conditions = parsed.Element("Conditions").Elements("Condition");
            conditions.Should().HaveCount(3);
        }

        [Fact]
        public void CreateFilter_SetsCombineMethodToAny()
        {
            // Arrange
            var tags = new[] { "Artist", "Album" };
            var query = "test";

            // Act
            var result = XmlFilterHelper.CreateFilter(tags, query, false, SearchSource.Library);

            // Assert
            var parsed = XElement.Parse(result);
            parsed.Element("Conditions").Attribute("CombineMethod").Value.Should().Be("Any");
        }

        [Theory]
        [InlineData(SearchSource.Library, "1")]
        [InlineData(SearchSource.Inbox, "2")]
        [InlineData(SearchSource.Podcasts, "4")]
        [InlineData(SearchSource.Audiobooks, "32")]
        [InlineData(SearchSource.Videos, "64")]
        public void CreateFilter_SetsCorrectSourceTypeForEachSource(SearchSource source, string expectedType)
        {
            // Arrange
            var tags = new[] { "Artist" };
            var query = "test";

            // Act
            var result = XmlFilterHelper.CreateFilter(tags, query, false, source);

            // Assert
            var parsed = XElement.Parse(result);
            parsed.Attribute("Type").Value.Should().Be(expectedType);
        }

        [Fact]
        public void CreateFilter_HandlesEmptyTagsArray()
        {
            // Arrange
            var tags = Array.Empty<string>();
            var query = "test";

            // Act
            var result = XmlFilterHelper.CreateFilter(tags, query, false, SearchSource.Library);

            // Assert
            var parsed = XElement.Parse(result);
            var conditions = parsed.Element("Conditions").Elements("Condition");
            conditions.Should().BeEmpty();
        }

        [Fact]
        public void CreateFilter_HandlesEmptyQuery()
        {
            // Arrange
            var tags = new[] { "Artist" };
            var query = "";

            // Act
            var result = XmlFilterHelper.CreateFilter(tags, query, false, SearchSource.Library);

            // Assert
            var parsed = XElement.Parse(result);
            var condition = parsed.Element("Conditions").Element("Condition");
            condition.Attribute("Value").Value.Should().BeEmpty();
        }

        [Fact]
        public void CreateFilter_HandlesSpecialCharactersInQuery()
        {
            // Arrange
            var tags = new[] { "Artist" };
            var query = "Test & <Special> \"Characters\"";

            // Act
            var result = XmlFilterHelper.CreateFilter(tags, query, false, SearchSource.Library);

            // Assert
            var parsed = XElement.Parse(result);
            var condition = parsed.Element("Conditions").Element("Condition");
            condition.Attribute("Value").Value.Should().Be("Test & <Special> \"Characters\"");
        }

        [Fact]
        public void CreateFilter_PreservesTagOrder()
        {
            // Arrange
            var tags = new[] { "Artist", "Album", "Title", "Genre" };
            var query = "test";

            // Act
            var result = XmlFilterHelper.CreateFilter(tags, query, false, SearchSource.Library);

            // Assert
            var parsed = XElement.Parse(result);
            var conditions = parsed.Element("Conditions").Elements("Condition").ToList();
            conditions[0].Attribute("Field").Value.Should().Be("Artist");
            conditions[1].Attribute("Field").Value.Should().Be("Album");
            conditions[2].Attribute("Field").Value.Should().Be("Title");
            conditions[3].Attribute("Field").Value.Should().Be("Genre");
        }

        [Fact]
        public void CreateFilter_AllConditionsHaveSameQuery()
        {
            // Arrange
            var tags = new[] { "Artist", "Album", "Title" };
            var query = "SearchTerm";

            // Act
            var result = XmlFilterHelper.CreateFilter(tags, query, false, SearchSource.Library);

            // Assert
            var parsed = XElement.Parse(result);
            var conditions = parsed.Element("Conditions").Elements("Condition");
            foreach (var condition in conditions)
            {
                condition.Attribute("Value").Value.Should().Be("SearchTerm");
            }
        }

        [Fact]
        public void CreateFilter_AllConditionsHaveSameComparison()
        {
            // Arrange
            var tags = new[] { "Artist", "Album", "Title" };
            var query = "test";

            // Act
            var result = XmlFilterHelper.CreateFilter(tags, query, isStrict: true, SearchSource.Library);

            // Assert
            var parsed = XElement.Parse(result);
            var conditions = parsed.Element("Conditions").Elements("Condition");
            foreach (var condition in conditions)
            {
                condition.Attribute("Comparison").Value.Should().Be("Is");
            }
        }

        [Fact]
        public void CreateFilter_OutputIsValidXmlString()
        {
            // Arrange
            var tags = new[] { "Artist", "Album" };
            var query = "test";

            // Act
            var result = XmlFilterHelper.CreateFilter(tags, query, false, SearchSource.Library);

            // Assert
            result.Should().StartWith("<Source");
            result.Should().EndWith("</Source>");
        }
    }
}
