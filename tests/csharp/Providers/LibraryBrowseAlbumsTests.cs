using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using MusicBeePlugin;
using MusicBeePlugin.Providers;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Providers
{
    /// <summary>
    /// MusicBee's album lookup table returns one row per album per artist, and
    /// the same (album artist, album) pair can come back more than once.
    /// <see cref="LibraryDataProvider.BrowseAlbums"/> collapses those to one
    /// entry, first occurrence wins, and every entry's count stays 1 - this is
    /// the browse list, not a track-count report. Clients page it by index, so
    /// a duplicate slipping through shifts every album after it.
    /// </summary>
    public class LibraryBrowseAlbumsTests
    {
        /// <summary>
        /// The lookup table's wire shape: entries separated by a double null,
        /// each entry "albumartist\0album".
        /// </summary>
        private static Plugin.MusicBeeApiInterface ApiReturning(params string[] entries)
        {
            return new Plugin.MusicBeeApiInterface
            {
                Library_QueryLookupTable = (key, types, query) => true,
                Library_QueryGetLookupTableValue = key => string.Join("\0\0", entries),
            };
        }

        private static string Entry(string albumArtist, string album)
        {
            return albumArtist + "\0" + album;
        }

        [Fact]
        public void BrowseAlbums_RepeatedAlbum_CollapsesToOneEntry()
        {
            var api = ApiReturning(
                Entry("Artist 1", "Album 1"),
                Entry("Artist 1", "Album 1"),
                Entry("Artist 1", "Album 1"));

            var albums = new LibraryDataProvider(api).BrowseAlbums();

            albums.Should().HaveCount(1);
            albums[0].artist.Should().Be("Artist 1");
            albums[0].album.Should().Be("Album 1");
        }

        [Fact]
        public void BrowseAlbums_CountIsAlwaysOne_NeverATrackCount()
        {
            var api = ApiReturning(
                Entry("Artist 1", "Album 1"),
                Entry("Artist 1", "Album 1"),
                Entry("Artist 2", "Album 2"));

            var albums = new LibraryDataProvider(api).BrowseAlbums();

            albums.Select(a => a.count).Should().AllBeEquivalentTo(
                1,
                "BrowseAlbums is a browse list; repeats must not accumulate into a count");
        }

        [Fact]
        public void BrowseAlbums_SameAlbumNameUnderDifferentArtists_StaysDistinct()
        {
            // The dedup key is the pair, not the album title: two artists with a
            // "Greatest Hits" are two albums.
            var api = ApiReturning(
                Entry("Artist 1", "Greatest Hits"),
                Entry("Artist 2", "Greatest Hits"));

            var albums = new LibraryDataProvider(api).BrowseAlbums();

            albums.Should().HaveCount(2);
            albums.Select(a => a.artist).Should().Equal("Artist 1", "Artist 2");
        }

        [Fact]
        public void BrowseAlbums_KeepsFirstSeenOrder()
        {
            // Clients page this list by index, so the order the rows arrive in is
            // the order they must be served in - deduplication must not reorder.
            var api = ApiReturning(
                Entry("Artist 3", "Album C"),
                Entry("Artist 1", "Album A"),
                Entry("Artist 3", "Album C"),
                Entry("Artist 2", "Album B"));

            var albums = new LibraryDataProvider(api).BrowseAlbums();

            albums.Select(a => a.album).Should().Equal("Album C", "Album A", "Album B");
        }

        [Fact]
        public void BrowseAlbums_QueryRefused_ReturnsEmptyRatherThanThrowing()
        {
            var api = new Plugin.MusicBeeApiInterface
            {
                Library_QueryLookupTable = (key, types, query) => false,
                Library_QueryGetLookupTableValue = key => string.Empty,
            };

            var albums = new LibraryDataProvider(api).BrowseAlbums();

            albums.Should().BeEmpty();
        }

        [Fact]
        public void BrowseAlbums_ReleasesTheLookupTable()
        {
            // The provider must hand the query back to MusicBee; leaving it open
            // makes the next lookup-table caller read this one's rows.
            var released = false;
            var api = new Plugin.MusicBeeApiInterface
            {
                Library_QueryLookupTable = (key, types, query) =>
                {
                    if (key == null && types == null && query == null)
                        released = true;
                    return true;
                },
                Library_QueryGetLookupTableValue = key => Entry("Artist 1", "Album 1"),
            };

            new LibraryDataProvider(api).BrowseAlbums();

            released.Should().BeTrue();
        }
    }
}
