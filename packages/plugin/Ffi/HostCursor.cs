using System.Collections.Generic;
using MusicBeePlugin.Ffi.Generated;

namespace MusicBeePlugin.Ffi
{
    /// <summary>The FFI calls that run one of MusicBee's query cursors.</summary>
    /// <remarks>
    ///     <c>Library_QueryFiles</c>, <c>Library_QueryLookupTable</c>,
    ///     <c>Playlist_QueryPlaylists</c>, <c>NowPlayingList_QueryFiles</c> and their
    ///     <c>Ex</c> forms set a query that is global to the MusicBee process, and the
    ///     calls after them read it. Two of these at once read each other's results, so
    ///     <see cref="NativeBridge" /> runs them one at a time. Every other call reaches
    ///     MusicBee directly and concurrently. A test runs each kind against a fake
    ///     MusicBee and holds these sets equal to the kinds that reach a query call.
    /// </remarks>
    public static class HostCursor
    {
        private static readonly HashSet<QueryType> CursorQueries = new HashSet<QueryType>
        {
            QueryType.PlaylistList,
            QueryType.PlaylistCatalog,
            QueryType.PlaylistTracks,
            QueryType.NowPlayingList,
            QueryType.NowPlayingListOrdered,
            QueryType.NowPlayingListOrder,
            QueryType.NowPlayingListPaths,
            QueryType.RadioStations,
            QueryType.LibraryBrowseGenres,
            QueryType.LibraryBrowseArtists,
            QueryType.LibraryBrowseAlbums,
            QueryType.LibraryBrowseTracks,
            QueryType.LibraryGenreArtists,
            QueryType.LibraryGenreTracks,
            QueryType.LibraryArtistAlbums,
            QueryType.LibraryAlbumTracks,
            QueryType.LibraryTrackPaths,
            QueryType.AlbumIdentifiers,
            QueryType.PodcastSubscriptions,
        };

        private static readonly HashSet<CommandType> CursorCommands = new HashSet<CommandType>
        {
            CommandType.LibraryPlayAll,
            CommandType.NowPlayingListPlay,
            CommandType.NowPlayingListSearch,
        };

        public static bool Runs(QueryType kind) => CursorQueries.Contains(kind);

        public static bool Runs(CommandType kind) => CursorCommands.Contains(kind);
    }
}
