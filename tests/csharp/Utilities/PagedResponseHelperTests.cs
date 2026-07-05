using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using MusicBeePlugin.Utilities.Data;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Utilities
{
    public class PagedResponseHelperTests
    {
        #region CreatePage - Basic Functionality

        [Fact]
        public void CreatePage_FirstPage_ReturnsCorrectData()
        {
            // Arrange
            var data = CreateTestData(100);

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset: 0, limit: 25);

            // Assert
            page.Data.Should().HaveCount(25);
            page.Offset.Should().Be(0);
            page.Limit.Should().Be(25);
            page.Total.Should().Be(100);
            page.Data.First().Should().Be("Item_0");
            page.Data.Last().Should().Be("Item_24");
        }

        [Fact]
        public void CreatePage_MiddlePage_ReturnsCorrectData()
        {
            // Arrange
            var data = CreateTestData(100);

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset: 50, limit: 25);

            // Assert
            page.Data.Should().HaveCount(25);
            page.Offset.Should().Be(50);
            page.Limit.Should().Be(25);
            page.Total.Should().Be(100);
            page.Data.First().Should().Be("Item_50");
            page.Data.Last().Should().Be("Item_74");
        }

        [Fact]
        public void CreatePage_LastPage_ReturnsRemainingItems()
        {
            // Arrange
            var data = CreateTestData(100);

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset: 90, limit: 25);

            // Assert
            page.Data.Should().HaveCount(10); // Only 10 items remaining
            page.Offset.Should().Be(90);
            page.Limit.Should().Be(25);
            page.Total.Should().Be(100);
            page.Data.First().Should().Be("Item_90");
            page.Data.Last().Should().Be("Item_99");
        }

        #endregion

        #region CreatePage - Boundary Conditions

        [Fact]
        public void CreatePage_OffsetBeyondTotal_ReturnsEmptyList()
        {
            // Arrange
            var data = CreateTestData(50);

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset: 100, limit: 25);

            // Assert
            page.Data.Should().BeEmpty();
            page.Offset.Should().Be(100);
            page.Limit.Should().Be(25);
            page.Total.Should().Be(50);
        }

        [Fact]
        public void CreatePage_OffsetEqualsTotal_ReturnsEmptyList()
        {
            // Arrange
            var data = CreateTestData(50);

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset: 50, limit: 25);

            // Assert
            page.Data.Should().BeEmpty();
            page.Total.Should().Be(50);
        }

        [Fact]
        public void CreatePage_ZeroOffset_ReturnsFromStart()
        {
            // Arrange
            var data = CreateTestData(10);

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset: 0, limit: 5);

            // Assert
            page.Data.Should().HaveCount(5);
            page.Data.First().Should().Be("Item_0");
        }

        [Fact]
        public void CreatePage_ZeroLimit_ReturnsEmptyList()
        {
            // Arrange
            var data = CreateTestData(50);

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset: 0, limit: 0);

            // Assert
            page.Data.Should().BeEmpty();
            page.Limit.Should().Be(0);
        }

        [Fact]
        public void CreatePage_LimitLargerThanDataset_ReturnsAllItems()
        {
            // Arrange
            var data = CreateTestData(10);

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset: 0, limit: 100);

            // Assert
            page.Data.Should().HaveCount(10);
            page.Limit.Should().Be(100);
            page.Total.Should().Be(10);
        }

        [Fact]
        public void CreatePage_SingleItem_Works()
        {
            // Arrange
            var data = new List<string> { "Only Item" };

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset: 0, limit: 10);

            // Assert
            page.Data.Should().HaveCount(1);
            page.Data.First().Should().Be("Only Item");
            page.Total.Should().Be(1);
        }

        #endregion

        #region CreatePage - Empty Dataset

        [Fact]
        public void CreatePage_EmptyDataset_ReturnsEmptyPage()
        {
            // Arrange
            var data = new List<string>();

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset: 0, limit: 25);

            // Assert
            page.Data.Should().BeEmpty();
            page.Total.Should().Be(0);
            page.Offset.Should().Be(0);
            page.Limit.Should().Be(25);
        }

        [Fact]
        public void CreatePage_EmptyDatasetWithOffset_ReturnsEmptyPage()
        {
            // Arrange
            var data = new List<string>();

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset: 50, limit: 25);

            // Assert
            page.Data.Should().BeEmpty();
            page.Total.Should().Be(0);
        }

        #endregion

        #region CreatePage - IEnumerable Overload

        [Fact]
        public void CreatePage_IEnumerable_FirstPage_Works()
        {
            // Arrange
            IEnumerable<string> data = CreateTestData(100);

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset: 0, limit: 25);

            // Assert
            page.Data.Should().HaveCount(25);
            page.Total.Should().Be(100);
        }

        [Fact]
        public void CreatePage_IEnumerable_MiddlePage_Works()
        {
            // Arrange
            IEnumerable<string> data = Enumerable.Range(0, 100).Select(i => $"Item_{i}");

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset: 40, limit: 20);

            // Assert
            page.Data.Should().HaveCount(20);
            page.Data.First().Should().Be("Item_40");
        }

        [Fact]
        public void CreatePage_IEnumerable_EmptyEnumerable_Works()
        {
            // Arrange
            IEnumerable<string> data = Enumerable.Empty<string>();

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset: 0, limit: 25);

            // Assert
            page.Data.Should().BeEmpty();
            page.Total.Should().Be(0);
        }

        #endregion

        #region CreatePagedMessage

        [Fact]
        public void CreatePagedMessage_ReturnsSocketMessageWithPage()
        {
            // Arrange
            var data = CreateTestData(50);

            // Act
            var message = PagedResponseHelper.CreatePagedMessage("libraryartists", data, offset: 0, limit: 25);

            // Assert
            message.Context.Should().Be("libraryartists");
            message.Data.Should().NotBeNull();
        }

        [Fact]
        public void CreatePagedMessage_FirstPage_HasCorrectData()
        {
            // Arrange
            var data = CreateTestData(100);

            // Act
            var message = PagedResponseHelper.CreatePagedMessage("test", data, offset: 0, limit: 50);

            // Assert
            message.Context.Should().Be("test");
            var pageData = message.Data as MusicBeePlugin.Models.Responses.Page<string>;
            pageData.Should().NotBeNull();
            pageData.Data.Should().HaveCount(50);
            pageData.Total.Should().Be(100);
        }

        [Fact]
        public void CreatePagedMessage_OffsetBeyondTotal_ReturnsEmptyData()
        {
            // Arrange
            var data = CreateTestData(30);

            // Act
            var message = PagedResponseHelper.CreatePagedMessage("test", data, offset: 50, limit: 25);

            // Assert
            var pageData = message.Data as MusicBeePlugin.Models.Responses.Page<string>;
            pageData.Data.Should().BeEmpty();
            pageData.Total.Should().Be(30);
        }

        #endregion

        #region CreatePage - Various Data Types

        [Fact]
        public void CreatePage_IntegerList_Works()
        {
            // Arrange
            var data = Enumerable.Range(1, 100).ToList();

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset: 0, limit: 10);

            // Assert
            page.Data.Should().HaveCount(10);
            page.Data.Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        }

        [Fact]
        public void CreatePage_ComplexObjectList_Works()
        {
            // Arrange
            var data = Enumerable.Range(0, 50)
                .Select(i => new TestItem { Id = i, Name = $"Item {i}" })
                .ToList();

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset: 10, limit: 5);

            // Assert
            page.Data.Should().HaveCount(5);
            page.Data.First().Id.Should().Be(10);
            page.Data.Last().Id.Should().Be(14);
        }

        #endregion

        #region Pagination Scenarios

        [Theory]
        [InlineData(0, 25, 25)]   // First page
        [InlineData(25, 25, 25)]  // Second page
        [InlineData(50, 25, 25)]  // Third page
        [InlineData(75, 25, 25)]  // Fourth page (complete)
        [InlineData(90, 25, 10)]  // Last partial page
        public void CreatePage_VariousPaginations_ReturnsCorrectCount(int offset, int limit, int expectedCount)
        {
            // Arrange
            var data = CreateTestData(100);

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset, limit);

            // Assert
            page.Data.Should().HaveCount(expectedCount);
        }

        [Theory]
        [InlineData(0, 10, "Item_0")]
        [InlineData(10, 10, "Item_10")]
        [InlineData(50, 10, "Item_50")]
        [InlineData(99, 10, "Item_99")]
        public void CreatePage_VariousOffsets_StartsAtCorrectItem(int offset, int limit, string expectedFirst)
        {
            // Arrange
            var data = CreateTestData(100);

            // Act
            var page = PagedResponseHelper.CreatePage(data, offset, limit);

            // Assert
            if (page.Data.Count > 0)
            {
                page.Data.First().Should().Be(expectedFirst);
            }
        }

        [Fact]
        public void CreatePage_IterateThroughAllPages_ReturnsAllItems()
        {
            // Arrange
            var data = CreateTestData(100);
            var allItems = new List<string>();
            var pageSize = 30;
            var offset = 0;

            // Act
            while (offset < data.Count)
            {
                var page = PagedResponseHelper.CreatePage(data, offset, pageSize);
                allItems.AddRange(page.Data);
                offset += pageSize;
            }

            // Assert
            allItems.Should().HaveCount(100);
            allItems.Should().BeEquivalentTo(data);
        }

        #endregion

        #region Test Helpers

        private static List<string> CreateTestData(int count)
        {
            return Enumerable.Range(0, count).Select(i => $"Item_{i}").ToList();
        }

        private sealed class TestItem
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        #endregion
    }
}
