using System.Collections.Generic;
using MusicBeePlugin.Ffi.Generated;

namespace MusicBeePlugin.Ffi
{
    /// <summary>The FFI calls that must reach MusicBee one at a time.</summary>
    /// <remarks>
    ///     <c>Library_QueryFiles</c>, <c>Library_QueryLookupTable</c>,
    ///     <c>Playlist_QueryPlaylists</c>, <c>NowPlayingList_QueryFiles</c> and their
    ///     <c>Ex</c> forms set a query global to the MusicBee process that the calls
    ///     after them read, so two walks at once read each other's results. A walk of
    ///     the now playing list also reads it by index, call after call, so a change to
    ///     the list, a playlist or a file's tags must not land in the middle of one, and
    ///     a change made of several calls must not interleave with another. Every other
    ///     call reaches MusicBee directly and concurrently. A test runs each kind
    ///     against a fake MusicBee and holds these sets equal to what the kinds call.
    /// </remarks>
    public static class SerialHostCalls
    {
        private static readonly HashSet<QueryType> Queries = new HashSet<QueryType>
        {
            QueryType.PlaylistList,
            QueryType.PlaylistCatalog,
            QueryType.PlaylistTracks,
            QueryType.PlaylistCreate,
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

        private static readonly HashSet<CommandType> Commands = new HashSet<CommandType>
        {
            CommandType.SetRating,
            CommandType.SetLfmRating,
            CommandType.PlaylistPlay,
            CommandType.PlaylistDelete,
            CommandType.PlaylistAppend,
            CommandType.PlaylistSetFiles,
            CommandType.LibraryPlayAll,
            CommandType.NowPlayingListPlay,
            CommandType.NowPlayingListMove,
            CommandType.NowPlayingListRemove,
            CommandType.NowPlayingListClear,
            CommandType.NowPlayingListSearch,
            CommandType.NowPlayingQueue,
            CommandType.NowPlayingTagChange,
        };

        public static bool Includes(QueryType kind) => Queries.Contains(kind);

        public static bool Includes(CommandType kind) => Commands.Contains(kind);
    }
}
