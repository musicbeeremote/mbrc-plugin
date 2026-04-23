//! Protocol constants matching the C# ProtocolConstants.cs exactly.

// Message format
pub const MESSAGE_TERMINATOR: &str = "\r\n";

// Protocol 2 — Basic functionality
pub const ERROR: &str = "error";
pub const PLAYER: &str = "player";
pub const PROTOCOL: &str = "protocol";
pub const PLAYER_NAME: &str = "MusicBee";
pub const PROTOCOL_VERSION: i32 = 4;
pub const PLUGIN_VERSION: &str = "pluginversion";
pub const NOT_ALLOWED: &str = "notallowed";

// Protocol 2 — API calls
pub const PLAYER_STATUS: &str = "playerstatus";
pub const PLAYER_REPEAT: &str = "playerrepeat";
pub const PLAYER_SCROBBLE: &str = "scrobbler";
pub const PLAYER_SHUFFLE: &str = "playershuffle";
pub const PLAYER_MUTE: &str = "playermute";
pub const PLAYER_PLAY_PAUSE: &str = "playerplaypause";
pub const PLAYER_PREVIOUS: &str = "playerprevious";
pub const PLAYER_NEXT: &str = "playernext";
pub const PLAYER_STOP: &str = "playerstop";
pub const PLAYER_STATE: &str = "playerstate";
pub const PLAYER_VOLUME: &str = "playervolume";
pub const PLAYER_AUTO_DJ: &str = "playerautodj";

pub const NOW_PLAYING_TRACK: &str = "nowplayingtrack";
pub const NOW_PLAYING_COVER: &str = "nowplayingcover";
pub const NOW_PLAYING_POSITION: &str = "nowplayingposition";
pub const NOW_PLAYING_LYRICS: &str = "nowplayinglyrics";
pub const NOW_PLAYING_RATING: &str = "nowplayingrating";
pub const NOW_PLAYING_LFM_RATING: &str = "nowplayinglfmrating";
pub const NOW_PLAYING_LIST: &str = "nowplayinglist";
pub const NOW_PLAYING_LIST_CHANGED: &str = "nowplayinglistchanged";
pub const NOW_PLAYING_LIST_PLAY: &str = "nowplayinglistplay";
pub const NOW_PLAYING_LIST_REMOVE: &str = "nowplayinglistremove";
pub const NOW_PLAYING_LIST_MOVE: &str = "nowplayinglistmove";
pub const NOW_PLAYING_LIST_SEARCH: &str = "nowplayinglistsearch";

pub const LIBRARY_SEARCH_ARTIST: &str = "librarysearchartist";
pub const LIBRARY_SEARCH_ALBUM: &str = "librarysearchalbum";
pub const LIBRARY_SEARCH_GENRE: &str = "librarysearchgenre";
pub const LIBRARY_SEARCH_TITLE: &str = "librarysearchtitle";

pub const LIBRARY_ARTIST_ALBUMS: &str = "libraryartistalbums";
pub const LIBRARY_GENRE_ARTISTS: &str = "librarygenreartists";
pub const LIBRARY_ALBUM_TRACKS: &str = "libraryalbumtracks";

pub const LIBRARY_QUEUE_GENRE: &str = "libraryqueuegenre";
pub const LIBRARY_QUEUE_ARTIST: &str = "libraryqueueartist";
pub const LIBRARY_QUEUE_ALBUM: &str = "libraryqueuealbum";
pub const LIBRARY_QUEUE_TRACK: &str = "libraryqueuetrack";

pub const PLAYLIST_LIST: &str = "playlistlist";

// Protocol 2.1
pub const PING: &str = "ping";
pub const PONG: &str = "pong";
pub const INIT: &str = "init";
pub const PLAYER_PLAY: &str = "playerplay";
pub const PLAYER_PAUSE: &str = "playerpause";

// Protocol 3
pub const PLAYLIST_PLAY: &str = "playlistplay";
pub const NO_BROADCAST: &str = "nobroadcast";
pub const LIBRARY_BROWSE_GENRES: &str = "browsegenres";
pub const LIBRARY_BROWSE_ARTISTS: &str = "browseartists";
pub const LIBRARY_BROWSE_ALBUMS: &str = "browsealbums";
pub const LIBRARY_BROWSE_TRACKS: &str = "browsetracks";
pub const NOW_PLAYING_QUEUE: &str = "nowplayingqueue";

// Protocol 4
pub const PLAYER_OUTPUT: &str = "playeroutput";
pub const VERIFY_CONNECTION: &str = "verifyconnection";
pub const PLAYER_OUTPUT_SWITCH: &str = "playeroutputswitch";
pub const RADIO_STATIONS: &str = "radiostations";
pub const NOW_PLAYING_DETAILS: &str = "nowplayingdetails";
pub const NOW_PLAYING_TAG_CHANGE: &str = "nowplayingtagchange";
pub const LIBRARY_PLAY_ALL: &str = "libraryplayall";
pub const LIBRARY_ALBUM_COVER: &str = "libraryalbumcover";
pub const LIBRARY_COVER_CACHE_BUILD_STATUS: &str = "librarycovercachebuildstatus";

// Protocol version numbers
pub const V2: i32 = 2;
pub const V3: i32 = 3;
pub const V4: i32 = 4;
