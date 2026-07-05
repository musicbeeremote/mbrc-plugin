using System.Collections.Generic;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Models.Entities;

namespace MusicBeeRemote.Core.Tests.Mocks
{
    /// <summary>
    ///     Mock implementation of ILibraryDataProvider for testing.
    ///     Allows verification of method calls and configuration of return values.
    /// </summary>
    public class MockLibraryDataProvider : ILibraryDataProvider
    {
        // Configurable data
        public List<RadioStation> RadioStations { get; set; } = new List<RadioStation>();
        public List<GenreData> Genres { get; set; } = new List<GenreData>();
        public List<ArtistData> Artists { get; set; } = new List<ArtistData>();
        public List<AlbumData> Albums { get; set; } = new List<AlbumData>();
        public List<Track> Tracks { get; set; } = new List<Track>();
        public Dictionary<string, string> AlbumIdentifiers { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> TrackPathsMap { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> FileModificationDates { get; set; } = new Dictionary<string, string>();
        public string DefaultArtwork { get; set; } = "/path/to/artwork.jpg";
        public byte[] DefaultArtworkData { get; set; } = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header

        // Call counters
        public int SearchGenresCallCount { get; private set; }
        public int SearchArtistsCallCount { get; private set; }
        public int SearchAlbumsCallCount { get; private set; }
        public int SearchTracksCallCount { get; private set; }

        public MockLibraryDataProvider()
        {
            // Add some default test data
            Genres.Add(new GenreData("Rock", 100));
            Genres.Add(new GenreData("Pop", 80));
            Genres.Add(new GenreData("Jazz", 30));

            Artists.Add(new ArtistData("Test Artist 1", 50));
            Artists.Add(new ArtistData("Test Artist 2", 30));

            Albums.Add(new AlbumData("Test Artist 1", "Test Album 1"));
            Albums.Add(new AlbumData("Test Artist 2", "Test Album 2"));

            Tracks.Add(new Track("Test Artist 1", "Test Track 1", 1, "/music/track1.mp3"));
            Tracks.Add(new Track("Test Artist 2", "Test Track 2", 2, "/music/track2.mp3"));
        }

        #region Radio Stations

        public List<RadioStation> GetRadioStations(int offset = 0, int limit = 4000) => RadioStations;

        #endregion

        #region Search Operations

        public IEnumerable<GenreData> SearchGenres(string query, SearchSource searchSource)
        {
            SearchGenresCallCount++;
            return Genres.FindAll(g => g.Genre.Contains(query));
        }

        public IEnumerable<ArtistData> SearchArtists(string query, SearchSource searchSource)
        {
            SearchArtistsCallCount++;
            return Artists.FindAll(a => a.Artist.Contains(query));
        }

        public List<AlbumData> SearchAlbums(string query, SearchSource searchSource)
        {
            SearchAlbumsCallCount++;
            return Albums.FindAll(a => a.Album.Contains(query));
        }

        public IEnumerable<Track> SearchTracks(string query, SearchSource searchSource)
        {
            SearchTracksCallCount++;
            return Tracks.FindAll(t => t.Title.Contains(query));
        }

        #endregion

        #region Browse Operations

        public IEnumerable<GenreData> BrowseGenres(int offset = 0, int limit = 4000) => Genres;

        public IEnumerable<ArtistData> BrowseArtists(int offset = 0, int limit = 4000, bool albumArtists = false) => Artists;

        public List<AlbumData> BrowseAlbums(int offset = 0, int limit = 4000) => Albums;

        public IEnumerable<Track> BrowseTracks(int offset = 0, int limit = 4000) => Tracks;

        #endregion

        #region Hierarchical Navigation

        public IEnumerable<Track> GetAlbumTracks(string album, SearchSource searchSource)
        {
            return Tracks.FindAll(t => t.Album == album);
        }

        public List<AlbumData> GetArtistAlbums(string artist, SearchSource searchSource)
        {
            return Albums.FindAll(a => a.Artist == artist);
        }

        public IEnumerable<ArtistData> GetGenreArtists(string genre, SearchSource searchSource)
        {
            // In a real implementation, this would filter by genre
            return Artists;
        }

        #endregion

        #region Queue Support

        public string[] GetFileUrlsByMetaTag(MetaTag tag, string query, SearchSource searchSource)
        {
            var results = new List<string>();
            foreach (var track in Tracks)
            {
                switch (tag)
                {
                    case MetaTag.Artist when track.Artist == query:
                    case MetaTag.Album when track.Album == query:
                    case MetaTag.Title when track.Title == query:
                        results.Add(track.Src);
                        break;
                }
            }
            return results.ToArray();
        }

        #endregion

        #region Artwork

        public string GetArtworkForTrack(string trackPath) => DefaultArtwork;

        public byte[] GetArtworkDataForTrack(string trackPath) => DefaultArtworkData;

        #endregion

        #region Album Cover Cache Support

        public Dictionary<string, string> GetAllAlbumIdentifiers() => AlbumIdentifiers;

        public Dictionary<string, string> GetTrackPaths() => TrackPathsMap;

        public Dictionary<string, string> GetFileModificationDates() => FileModificationDates;

        #endregion

        #region Track Metadata

        public string GetAlbumArtistForTrack(string trackPath)
        {
            var track = Tracks.Find(t => t.Src == trackPath);
            return track?.AlbumArtist ?? track?.Artist ?? string.Empty;
        }

        public string GetAlbumForTrack(string trackPath)
        {
            var track = Tracks.Find(t => t.Src == trackPath);
            return track?.Album ?? string.Empty;
        }

        public Dictionary<string, (string Artist, string Album)> GetBatchTrackMetadata(IEnumerable<string> trackPaths)
        {
            var result = new Dictionary<string, (string Artist, string Album)>();
            foreach (var path in trackPaths)
            {
                var track = Tracks.Find(t => t.Src == path);
                if (track != null)
                {
                    result[path] = (track.AlbumArtist ?? track.Artist, track.Album);
                }
            }
            return result;
        }

        #endregion
    }
}
