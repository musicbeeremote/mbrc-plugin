using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Models.Responses;
using MusicBeePlugin.Protocol.Messages;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Integration
{
    /// <summary>
    ///     Integration tests for paginated library requests.
    ///     Tests the full flow: connect -> handshake -> paginated requests -> disconnect
    /// </summary>
    public class PaginationIntegrationTests : IAsyncLifetime
    {
        private SocketServerFixture _fixture;

        public Task InitializeAsync()
        {
            _fixture = new SocketServerFixture();
            return _fixture.InitializeAsync();
        }

        public Task DisposeAsync()
        {
            return _fixture.DisposeAsync();
        }

        #region Artist Pagination Tests

        [Fact]
        public async Task BrowseArtists_FetchAllPages_ReturnsAll300Artists()
        {
            // Arrange
            const int limit = 50;
            var allArtists = new List<string>();

            using (var client = await SocketTestClient.ConnectAsync(_fixture.Port))
            {
                await client.HandshakeAsync();

                // Act - fetch all pages
                var offset = 0;
                long total;

                do
                {
                    var requestData = new { offset, limit };
                    var response = await client.SendAndReceiveAsync(ProtocolConstants.LibraryBrowseArtists, requestData);

                    var pageData = GetPage<ArtistData>(response);
                    total = pageData.Total;

                    foreach (var artist in pageData.Data)
                    {
                        allArtists.Add(artist.Artist);
                    }

                    offset += limit;
                } while (offset < total);
            }

            // Assert
            allArtists.Should().HaveCount(300);
            allArtists.Should().Contain("Test Artist 001");
            allArtists.Should().Contain("Test Artist 150");
            allArtists.Should().Contain("Test Artist 300");
        }

        [Fact]
        public async Task BrowseArtists_FirstPage_ReturnsCorrectMetadata()
        {
            // Arrange
            Page<ArtistData> pageData;

            using (var client = await SocketTestClient.ConnectAsync(_fixture.Port))
            {
                await client.HandshakeAsync();

                // Act
                var requestData = new { offset = 0, limit = 50 };
                var response = await client.SendAndReceiveAsync(ProtocolConstants.LibraryBrowseArtists, requestData);

                pageData = GetPage<ArtistData>(response);
            }

            // Assert
            pageData.Total.Should().Be(300);
            pageData.Offset.Should().Be(0);
            pageData.Limit.Should().Be(50);
            pageData.Data.Should().HaveCount(50);
            pageData.Data[0].Artist.Should().Be("Test Artist 001");
            pageData.Data[49].Artist.Should().Be("Test Artist 050");
        }

        [Fact]
        public async Task BrowseArtists_MiddlePage_ReturnsCorrectData()
        {
            // Arrange
            Page<ArtistData> pageData;

            using (var client = await SocketTestClient.ConnectAsync(_fixture.Port))
            {
                await client.HandshakeAsync();

                // Act - fetch page 3 (offset 100)
                var requestData = new { offset = 100, limit = 50 };
                var response = await client.SendAndReceiveAsync(ProtocolConstants.LibraryBrowseArtists, requestData);

                pageData = GetPage<ArtistData>(response);
            }

            // Assert
            pageData.Total.Should().Be(300);
            pageData.Offset.Should().Be(100);
            pageData.Limit.Should().Be(50);
            pageData.Data.Should().HaveCount(50);
            pageData.Data[0].Artist.Should().Be("Test Artist 101");
            pageData.Data[49].Artist.Should().Be("Test Artist 150");
        }

        [Fact]
        public async Task BrowseArtists_LastPage_ReturnsRemainingItems()
        {
            // Arrange
            Page<ArtistData> pageData;

            using (var client = await SocketTestClient.ConnectAsync(_fixture.Port))
            {
                await client.HandshakeAsync();

                // Act - fetch last page (offset 250)
                var requestData = new { offset = 250, limit = 50 };
                var response = await client.SendAndReceiveAsync(ProtocolConstants.LibraryBrowseArtists, requestData);

                pageData = GetPage<ArtistData>(response);
            }

            // Assert
            pageData.Total.Should().Be(300);
            pageData.Offset.Should().Be(250);
            pageData.Limit.Should().Be(50);
            pageData.Data.Should().HaveCount(50);
            pageData.Data[0].Artist.Should().Be("Test Artist 251");
            pageData.Data[49].Artist.Should().Be("Test Artist 300");
        }

        [Fact]
        public async Task BrowseArtists_OffsetBeyondTotal_ReturnsEmptyData()
        {
            // Arrange
            Page<ArtistData> pageData;

            using (var client = await SocketTestClient.ConnectAsync(_fixture.Port))
            {
                await client.HandshakeAsync();

                // Act - fetch beyond available data
                var requestData = new { offset = 500, limit = 50 };
                var response = await client.SendAndReceiveAsync(ProtocolConstants.LibraryBrowseArtists, requestData);

                pageData = GetPage<ArtistData>(response);
            }

            // Assert
            pageData.Total.Should().Be(300);
            pageData.Offset.Should().Be(500);
            pageData.Data.Should().BeEmpty();
        }

        #endregion

        #region Album Pagination Tests

        [Fact]
        public async Task BrowseAlbums_FetchAllPages_ReturnsAll300Albums()
        {
            // Arrange
            const int limit = 50;
            var allAlbums = new List<string>();

            using (var client = await SocketTestClient.ConnectAsync(_fixture.Port))
            {
                await client.HandshakeAsync();

                // Act - fetch all pages
                var offset = 0;
                long total;

                do
                {
                    var requestData = new { offset, limit };
                    var response = await client.SendAndReceiveAsync(ProtocolConstants.LibraryBrowseAlbums, requestData);

                    var pageData = GetPage<AlbumData>(response);
                    total = pageData.Total;

                    foreach (var album in pageData.Data)
                    {
                        allAlbums.Add(album.Album);
                    }

                    offset += limit;
                } while (offset < total);
            }

            // Assert
            allAlbums.Should().HaveCount(300);
        }

        #endregion

        #region Track Pagination Tests

        [Fact]
        public async Task BrowseTracks_FetchAllPages_ReturnsAll300Tracks()
        {
            // Arrange
            const int limit = 50;
            var allTracks = new List<string>();

            using (var client = await SocketTestClient.ConnectAsync(_fixture.Port))
            {
                await client.HandshakeAsync();

                // Act - fetch all pages
                var offset = 0;
                long total;

                do
                {
                    var requestData = new { offset, limit };
                    var response = await client.SendAndReceiveAsync(ProtocolConstants.LibraryBrowseTracks, requestData);

                    var pageData = GetPage<Track>(response);
                    total = pageData.Total;

                    foreach (var track in pageData.Data)
                    {
                        allTracks.Add(track.Title);
                    }

                    offset += limit;
                } while (offset < total);
            }

            // Assert
            allTracks.Should().HaveCount(300);
        }

        #endregion

        #region Genre Pagination Tests

        [Fact]
        public async Task BrowseGenres_FetchAllPages_ReturnsAll50Genres()
        {
            // Arrange
            const int limit = 20;
            var allGenres = new List<string>();

            using (var client = await SocketTestClient.ConnectAsync(_fixture.Port))
            {
                await client.HandshakeAsync();

                // Act - fetch all pages
                var offset = 0;
                long total;

                do
                {
                    var requestData = new { offset, limit };
                    var response = await client.SendAndReceiveAsync(ProtocolConstants.LibraryBrowseGenres, requestData);

                    var pageData = GetPage<GenreData>(response);
                    total = pageData.Total;

                    foreach (var genre in pageData.Data)
                    {
                        allGenres.Add(genre.Genre);
                    }

                    offset += limit;
                } while (offset < total);
            }

            // Assert
            allGenres.Should().HaveCount(50);
            allGenres.Should().Contain("Test Genre 01");
            allGenres.Should().Contain("Test Genre 25");
            allGenres.Should().Contain("Test Genre 50");
        }

        [Fact]
        public async Task BrowseGenres_FirstPage_ReturnsCorrectMetadata()
        {
            // Arrange
            Page<GenreData> pageData;

            using (var client = await SocketTestClient.ConnectAsync(_fixture.Port))
            {
                await client.HandshakeAsync();

                // Act
                var requestData = new { offset = 0, limit = 20 };
                var response = await client.SendAndReceiveAsync(ProtocolConstants.LibraryBrowseGenres, requestData);

                pageData = GetPage<GenreData>(response);
            }

            // Assert
            pageData.Total.Should().Be(50);
            pageData.Offset.Should().Be(0);
            pageData.Limit.Should().Be(20);
            pageData.Data.Should().HaveCount(20);
            pageData.Data[0].Genre.Should().Be("Test Genre 01");
            pageData.Data[19].Genre.Should().Be("Test Genre 20");
        }

        #endregion

        #region Connection Persistence Tests

        [Fact]
        public async Task MultipleRequests_SameConnection_AllSucceed()
        {
            // Arrange
            var responses = new List<Page<ArtistData>>();

            using (var client = await SocketTestClient.ConnectAsync(_fixture.Port))
            {
                await client.HandshakeAsync();

                // Act - make multiple requests on the same connection
                for (var page = 0; page < 6; page++)
                {
                    var requestData = new { offset = page * 50, limit = 50 };
                    var response = await client.SendAndReceiveAsync(ProtocolConstants.LibraryBrowseArtists, requestData);
                    responses.Add(GetPage<ArtistData>(response));
                }
            }

            // Assert - all pages should have been received successfully
            responses.Should().HaveCount(6);
            for (var i = 0; i < 6; i++)
            {
                responses[i].Offset.Should().Be(i * 50);
                responses[i].Data.Should().HaveCount(50);
            }
        }

        [Fact]
        public async Task MixedRequests_DifferentEntityTypes_AllSucceed()
        {
            // Arrange
            Page<ArtistData> artists;
            Page<AlbumData> albums;
            Page<Track> tracks;
            Page<GenreData> genres;

            using (var client = await SocketTestClient.ConnectAsync(_fixture.Port))
            {
                await client.HandshakeAsync();

                // Act - request different entity types
                var artistResponse = await client.SendAndReceiveAsync(ProtocolConstants.LibraryBrowseArtists, new { offset = 0, limit = 50 });
                var albumResponse = await client.SendAndReceiveAsync(ProtocolConstants.LibraryBrowseAlbums, new { offset = 0, limit = 50 });
                var trackResponse = await client.SendAndReceiveAsync(ProtocolConstants.LibraryBrowseTracks, new { offset = 0, limit = 50 });
                var genreResponse = await client.SendAndReceiveAsync(ProtocolConstants.LibraryBrowseGenres, new { offset = 0, limit = 20 });

                artists = GetPage<ArtistData>(artistResponse);
                albums = GetPage<AlbumData>(albumResponse);
                tracks = GetPage<Track>(trackResponse);
                genres = GetPage<GenreData>(genreResponse);
            }

            // Assert
            artists.Total.Should().Be(300);
            albums.Total.Should().Be(300);
            tracks.Total.Should().Be(300);
            genres.Total.Should().Be(50);
        }

        #endregion

        #region Helper Methods

        private static Page<T> GetPage<T>(SocketMessage message)
        {
            if (message.Data is JToken token)
            {
                return token.ToObject<Page<T>>();
            }

            return null;
        }

        #endregion
    }
}
