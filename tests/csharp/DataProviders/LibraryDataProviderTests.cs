using System.Collections.Generic;
using System.Linq;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Models.Entities;
using MusicBeeRemote.Core.Tests.Mocks;
using Xunit;

namespace MusicBeeRemote.Core.Tests.DataProviders
{
    /// <summary>
    ///     Tests for ILibraryDataProvider interface behavior using mock implementation.
    ///     Tests verify the contract behavior that implementations must follow.
    /// </summary>
    public class LibraryDataProviderTests
    {
        private readonly MockLibraryDataProvider _provider;

        public LibraryDataProviderTests()
        {
            _provider = new MockLibraryDataProvider();
        }

        #region Search Operations Tests

        [Fact]
        public void SearchGenres_ReturnsMatchingGenres()
        {
            var results = _provider.SearchGenres("Rock", SearchSource.Library).ToList();

            Assert.Single(results);
            Assert.Equal("Rock", results[0].Genre);
            Assert.Equal(1, _provider.SearchGenresCallCount);
        }

        [Fact]
        public void SearchGenres_NoMatch_ReturnsEmpty()
        {
            var results = _provider.SearchGenres("NonExistent", SearchSource.Library).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public void SearchArtists_ReturnsMatchingArtists()
        {
            var results = _provider.SearchArtists("Test Artist 1", SearchSource.Library).ToList();

            Assert.Single(results);
            Assert.Equal("Test Artist 1", results[0].Artist);
            Assert.Equal(1, _provider.SearchArtistsCallCount);
        }

        [Fact]
        public void SearchAlbums_ReturnsMatchingAlbums()
        {
            var results = _provider.SearchAlbums("Album 1", SearchSource.Library);

            Assert.Single(results);
            Assert.Contains("Album 1", results[0].Album);
            Assert.Equal(1, _provider.SearchAlbumsCallCount);
        }

        [Fact]
        public void SearchTracks_ReturnsMatchingTracks()
        {
            var results = _provider.SearchTracks("Track 1", SearchSource.Library).ToList();

            Assert.Single(results);
            Assert.Contains("Track 1", results[0].Title);
            Assert.Equal(1, _provider.SearchTracksCallCount);
        }

        #endregion

        #region Browse Operations Tests

        [Fact]
        public void BrowseGenres_ReturnsAllGenres()
        {
            var genres = _provider.BrowseGenres().ToList();

            Assert.Equal(3, genres.Count);
            Assert.Contains(genres, g => g.Genre == "Rock");
            Assert.Contains(genres, g => g.Genre == "Pop");
            Assert.Contains(genres, g => g.Genre == "Jazz");
        }

        [Fact]
        public void BrowseGenres_GenresHaveCounts()
        {
            var genres = _provider.BrowseGenres().ToList();

            var rock = genres.Find(g => g.Genre == "Rock");
            Assert.Equal(100, rock.Count);
        }

        [Fact]
        public void BrowseArtists_ReturnsAllArtists()
        {
            var artists = _provider.BrowseArtists().ToList();

            Assert.Equal(2, artists.Count);
        }

        [Fact]
        public void BrowseAlbums_ReturnsAllAlbums()
        {
            var albums = _provider.BrowseAlbums();

            Assert.Equal(2, albums.Count);
        }

        [Fact]
        public void BrowseTracks_ReturnsAllTracks()
        {
            var tracks = _provider.BrowseTracks().ToList();

            Assert.Equal(2, tracks.Count);
            Assert.Contains(tracks, t => t.Title == "Test Track 1");
            Assert.Contains(tracks, t => t.Title == "Test Track 2");
        }

        #endregion

        #region Hierarchical Navigation Tests

        [Fact]
        public void GetAlbumTracks_ReturnsTracksForAlbum()
        {
            _provider.Tracks.Clear();
            _provider.Tracks.Add(new Track { Title = "Track 1", Album = "Album A", Src = "/a.mp3" });
            _provider.Tracks.Add(new Track { Title = "Track 2", Album = "Album A", Src = "/b.mp3" });
            _provider.Tracks.Add(new Track { Title = "Track 3", Album = "Album B", Src = "/c.mp3" });

            var tracks = _provider.GetAlbumTracks("Album A", SearchSource.Library).ToList();

            Assert.Equal(2, tracks.Count);
            Assert.All(tracks, t => Assert.Equal("Album A", t.Album));
        }

        [Fact]
        public void GetArtistAlbums_ReturnsAlbumsForArtist()
        {
            _provider.Albums.Clear();
            _provider.Albums.Add(new AlbumData("Artist A", "Album 1"));
            _provider.Albums.Add(new AlbumData("Artist A", "Album 2"));
            _provider.Albums.Add(new AlbumData("Artist B", "Album 3"));

            var albums = _provider.GetArtistAlbums("Artist A", SearchSource.Library);

            Assert.Equal(2, albums.Count);
            Assert.All(albums, a => Assert.Equal("Artist A", a.Artist));
        }

        [Fact]
        public void GetGenreArtists_ReturnsArtists()
        {
            var artists = _provider.GetGenreArtists("Rock", SearchSource.Library).ToList();

            Assert.NotEmpty(artists);
        }

        #endregion

        #region Queue Support Tests

        [Fact]
        public void GetFileUrlsByMetaTag_Artist_ReturnsMatchingFiles()
        {
            _provider.Tracks.Clear();
            _provider.Tracks.Add(new Track { Artist = "Artist A", Title = "Track 1", Src = "/a.mp3" });
            _provider.Tracks.Add(new Track { Artist = "Artist A", Title = "Track 2", Src = "/b.mp3" });
            _provider.Tracks.Add(new Track { Artist = "Artist B", Title = "Track 3", Src = "/c.mp3" });

            var files = _provider.GetFileUrlsByMetaTag(MetaTag.Artist, "Artist A", SearchSource.Library);

            Assert.Equal(2, files.Length);
            Assert.Contains("/a.mp3", files);
            Assert.Contains("/b.mp3", files);
        }

        [Fact]
        public void GetFileUrlsByMetaTag_Album_ReturnsMatchingFiles()
        {
            _provider.Tracks.Clear();
            _provider.Tracks.Add(new Track { Album = "Album X", Title = "Track 1", Src = "/x.mp3" });

            var files = _provider.GetFileUrlsByMetaTag(MetaTag.Album, "Album X", SearchSource.Library);

            Assert.Single(files);
            Assert.Equal("/x.mp3", files[0]);
        }

        [Fact]
        public void GetFileUrlsByMetaTag_Title_ReturnsMatchingFiles()
        {
            _provider.Tracks.Clear();
            _provider.Tracks.Add(new Track { Title = "My Song", Src = "/song.mp3" });

            var files = _provider.GetFileUrlsByMetaTag(MetaTag.Title, "My Song", SearchSource.Library);

            Assert.Single(files);
        }

        #endregion

        #region Artwork Tests

        [Fact]
        public void GetArtworkForTrack_ReturnsPath()
        {
            _provider.DefaultArtwork = "/art/cover.png";

            var artwork = _provider.GetArtworkForTrack("/music/track.mp3");

            Assert.Equal("/art/cover.png", artwork);
        }

        [Fact]
        public void GetArtworkDataForTrack_ReturnsBytes()
        {
            var data = new byte[] { 1, 2, 3, 4 };
            _provider.DefaultArtworkData = data;

            var result = _provider.GetArtworkDataForTrack("/music/track.mp3");

            Assert.Equal(data, result);
        }

        #endregion

        #region Album Cache Support Tests

        [Fact]
        public void GetAllAlbumIdentifiers_ReturnsConfiguredIdentifiers()
        {
            _provider.AlbumIdentifiers["key1"] = "Artist1|Album1";
            _provider.AlbumIdentifiers["key2"] = "Artist2|Album2";

            var identifiers = _provider.GetAllAlbumIdentifiers();

            Assert.Equal(2, identifiers.Count);
            Assert.Equal("Artist1|Album1", identifiers["key1"]);
        }

        [Fact]
        public void GetTrackPaths_ReturnsConfiguredPaths()
        {
            _provider.TrackPathsMap["key1"] = "/music/track1.mp3";

            var paths = _provider.GetTrackPaths();

            Assert.Single(paths);
            Assert.Equal("/music/track1.mp3", paths["key1"]);
        }

        [Fact]
        public void GetFileModificationDates_ReturnsDates()
        {
            _provider.FileModificationDates["key1"] = "2024-01-15";

            var dates = _provider.GetFileModificationDates();

            Assert.Single(dates);
            Assert.Equal("2024-01-15", dates["key1"]);
        }

        #endregion

        #region Track Metadata Tests

        [Fact]
        public void GetAlbumArtistForTrack_ReturnsArtist()
        {
            _provider.Tracks.Clear();
            _provider.Tracks.Add(new Track
            {
                Src = "/music/song.mp3",
                AlbumArtist = "Album Artist Name",
                Artist = "Track Artist"
            });

            var artist = _provider.GetAlbumArtistForTrack("/music/song.mp3");

            Assert.Equal("Album Artist Name", artist);
        }

        [Fact]
        public void GetAlbumArtistForTrack_FallsBackToArtist()
        {
            _provider.Tracks.Clear();
            _provider.Tracks.Add(new Track
            {
                Src = "/music/song.mp3",
                AlbumArtist = null,
                Artist = "Track Artist"
            });

            var artist = _provider.GetAlbumArtistForTrack("/music/song.mp3");

            Assert.Equal("Track Artist", artist);
        }

        [Fact]
        public void GetAlbumForTrack_ReturnsAlbum()
        {
            _provider.Tracks.Clear();
            _provider.Tracks.Add(new Track
            {
                Src = "/music/song.mp3",
                Album = "My Album"
            });

            var album = _provider.GetAlbumForTrack("/music/song.mp3");

            Assert.Equal("My Album", album);
        }

        [Fact]
        public void GetBatchTrackMetadata_ReturnsMetadataForAllTracks()
        {
            _provider.Tracks.Clear();
            _provider.Tracks.Add(new Track { Src = "/a.mp3", AlbumArtist = "Artist A", Album = "Album A" });
            _provider.Tracks.Add(new Track { Src = "/b.mp3", AlbumArtist = "Artist B", Album = "Album B" });

            var paths = new List<string> { "/a.mp3", "/b.mp3" };
            var metadata = _provider.GetBatchTrackMetadata(paths);

            Assert.Equal(2, metadata.Count);
            Assert.Equal("Artist A", metadata["/a.mp3"].Artist);
            Assert.Equal("Album B", metadata["/b.mp3"].Album);
        }

        #endregion

        #region Radio Station Tests

        [Fact]
        public void GetRadioStations_ReturnsConfiguredStations()
        {
            _provider.RadioStations.Add(new RadioStation { Name = "Station 1", Url = "http://radio1" });
            _provider.RadioStations.Add(new RadioStation { Name = "Station 2", Url = "http://radio2" });

            var stations = _provider.GetRadioStations();

            Assert.Equal(2, stations.Count);
        }

        [Fact]
        public void GetRadioStations_EmptyByDefault()
        {
            var stations = _provider.GetRadioStations();
            Assert.Empty(stations);
        }

        #endregion
    }
}
