using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using AwesomeAssertions;
using MusicBeePlugin;
using MusicBeePlugin.Providers;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Providers
{
    /// <summary>
    /// MusicBee's filtered queries leave banned tracks out while the unfiltered
    /// listing keeps them. The drill-downs add them back by matching tags the way
    /// the filter would have.
    /// </summary>
    public class LibraryBannedTracksTests
    {
        private sealed class FakeLibrary
        {
            private readonly List<string> _paths = new List<string>();

            private readonly Dictionary<string, Dictionary<Plugin.MetaDataType, string>> _tags =
                new Dictionary<string, Dictionary<Plugin.MetaDataType, string>>();

            private readonly HashSet<string> _banned = new HashSet<string>();
            private Queue<string> _cursor = new Queue<string>();

            public int Listings { get; private set; }

            public FakeLibrary Add(string path, string albumArtist, string album, int track,
                string artist = null, string artists = null, string credits = null, bool banned = false)
            {
                _paths.Add(path);
                _tags[path] = new Dictionary<Plugin.MetaDataType, string>
                {
                    [Plugin.MetaDataType.AlbumArtist] = albumArtist,
                    [Plugin.MetaDataType.Album] = album,
                    [Plugin.MetaDataType.TrackNo] = track.ToString(),
                    [Plugin.MetaDataType.DiscNo] = "1",
                    [Plugin.MetaDataType.Artist] = artist ?? albumArtist,
                    [Plugin.MetaDataType.Artists] = artists ?? string.Empty,
                    [Plugin.MetaDataType.ArtistsWithArtistRole] = credits ?? string.Empty,
                    [Plugin.MetaDataType.Genre] = "Metal",
                    [Plugin.MetaDataType.TrackTitle] = path,
                };
                if (banned)
                    _banned.Add(path);
                return this;
            }

            public Plugin.MusicBeeApiInterface Api()
            {
                return new Plugin.MusicBeeApiInterface
                {
                    Library_QueryFilesEx = QueryFilesEx,
                    Library_QueryFiles = filter =>
                    {
                        _cursor = new Queue<string>(Kept().Where(p => Matches(p, filter)));
                        return true;
                    },
                    Library_QueryGetNextFile = () => _cursor.Count > 0 ? _cursor.Dequeue() : null,
                    Library_GetFileTags = GetFileTags,
                    Library_QueryLookupTable = (keys, values, filter) =>
                    {
                        if (filter == null)
                            return false;
                        _cursor = new Queue<string>(Kept().Where(p => Matches(p, filter))
                            .GroupBy(p => _tags[p][Plugin.MetaDataType.Artist])
                            .Select(g => g.Key + "\0" + g.Count()));
                        return true;
                    },
                    Library_QueryGetLookupTableValue = key => string.Join("\0\0", _cursor),
                };
            }

            private bool QueryFilesEx(string query, out string[] files)
            {
                Listings++;
                files = (query == null ? _paths : Kept()).ToArray();
                return true;
            }

            private bool GetFileTags(string path, Plugin.MetaDataType[] fields, out string[] results)
            {
                var tags = _tags[path];
                results = fields.Select(f => tags.TryGetValue(f, out var v) ? v : string.Empty).ToArray();
                return true;
            }

            private IEnumerable<string> Kept() => _paths.Where(p => !_banned.Contains(p));

            private bool Matches(string path, string filter)
            {
                var tags = _tags[path];
                var condition = XElement.Parse(filter).Descendants("Condition").Single();
                var value = (string)condition.Attribute("Value");
                switch ((string)condition.Attribute("Field"))
                {
                    case "Album":
                        return tags[Plugin.MetaDataType.Album] == value;
                    case "Genre":
                        return Split(tags[Plugin.MetaDataType.Genre]).Contains(value);
                    default:
                        return Split(tags[Plugin.MetaDataType.Artist]).Contains(value)
                               || Split(tags[Plugin.MetaDataType.Artists]).Contains(value);
                }
            }

            private static string[] Split(string tag) => tag.Split(new[] { "; " }, StringSplitOptions.None);
        }

        [Fact]
        public void AlbumTracks_ListBannedTracksInTrackOrder()
        {
            var library = new FakeLibrary()
                .Add("intro.mp3", "The Absence", "From Your Grave", 1, banned: true)
                .Add("necropolis.mp3", "The Absence", "From Your Grave", 3);

            var tracks = new LibraryDataProvider(library.Api()).GetAlbumTracks("From Your Grave").ToList();

            tracks.Select(t => t.src).Should().Equal("intro.mp3", "necropolis.mp3");
        }

        [Fact]
        public void AlbumTracks_KeepAlbumsSharingANameApart()
        {
            var library = new FakeLibrary()
                .Add("a1.mp3", "Artist A", "Greatest Hits", 1)
                .Add("a2.mp3", "Artist A", "Greatest Hits", 2)
                .Add("b1.mp3", "Artist B", "Greatest Hits", 1)
                .Add("b2.mp3", "Artist B", "Greatest Hits", 2, banned: true);

            var tracks = new LibraryDataProvider(library.Api()).GetAlbumTracks("Greatest Hits").ToList();

            tracks.Select(t => t.src).Should().Equal("a1.mp3", "a2.mp3", "b1.mp3", "b2.mp3");
        }

        [Fact]
        public void AlbumTracks_DoNotSplitAnAlbumName()
        {
            var library = new FakeLibrary()
                .Add("live.mp3", "Band", "Live", 1)
                .Add("unplugged.mp3", "Band", "Live; Unplugged", 1, banned: true);

            var tracks = new LibraryDataProvider(library.Api()).GetAlbumTracks("Live").ToList();

            tracks.Select(t => t.src).Should().Equal("live.mp3");
        }

        [Fact]
        public void ArtistAlbums_MatchTrackArtistsNotAlbumArtist()
        {
            var library = new FakeLibrary()
                .Add("own.mp3", "Guest", "Own Album", 1)
                .Add("comp.mp3", "Guest", "Compilation", 1, artist: "Someone Else", banned: true)
                .Add("feat.mp3", "Host", "Duets", 1, artist: "Host feat. Guest", artists: "Host; Guest", banned: true);

            var albums = new LibraryDataProvider(library.Api()).GetArtistAlbums("Guest");

            albums.Select(a => a.album).Should().BeEquivalentTo("Own Album", "Duets");
        }

        [Fact]
        public void GenreArtists_CreditEachSplitArtistOfABannedTrack()
        {
            var library = new FakeLibrary()
                .Add("a.mp3", "A", "One", 1)
                .Add("ab1.mp3", "A", "Two", 1, artist: "A & B", credits: "A; B", banned: true)
                .Add("ab2.mp3", "A", "Two", 2, artist: "A & B", credits: "A; B", banned: true);

            var artists = new LibraryDataProvider(library.Api()).GetGenreArtists("Metal").ToList();

            artists.Should().HaveCount(2);
            artists.Single(a => a.artist == "A").count.Should().Be(3);
            artists.Single(a => a.artist == "B").count.Should().Be(2);
        }

        [Fact]
        public void ExcludedPaths_AreListedOnceUntilForgotten()
        {
            var library = new FakeLibrary()
                .Add("intro.mp3", "The Absence", "From Your Grave", 1, banned: true)
                .Add("necropolis.mp3", "The Absence", "From Your Grave", 3);
            var provider = new LibraryDataProvider(library.Api());

            provider.GetExcludedPaths().Should().Equal("intro.mp3");
            provider.GetAlbumTracks("From Your Grave").ToList();
            library.Listings.Should().Be(2, "one unfiltered and one filtered listing");

            provider.ForgetExcludedPaths();
            provider.GetExcludedPaths();
            library.Listings.Should().Be(4);
        }
    }
}
