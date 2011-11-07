using System;
using System.Runtime.InteropServices;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        public const short PluginInfoVersion = 1;
        public const short MinInterfaceVersion = 5;
        public const short MinApiRevision = 8;

        [StructLayout(LayoutKind.Sequential)]
        public struct MusicBeeApiInterface
        {
            public short InterfaceVersion;
            public short ApiRevision;
            public MB_ReleaseStringDelegate MB_ReleaseString;
            public MB_TraceDelegate MB_Trace;
            public Setting_GetPersistentStoragePathDelegate Setting_GetPersistentStoragePath;
            public Setting_GetSkinDelegate Setting_GetSkin;
            public Setting_GetSkinElementColourDelegate Setting_GetSkinElementColour;
            public Setting_IsWindowBordersSkinnedDelegate Setting_IsWindowBordersSkinned;
            public Library_GetFilePropertyDelegate Library_GetFileProperty;
            public Library_GetFileTagDelegate Library_GetFileTag;
            public Library_SetFileTagDelegate Library_SetFileTag;
            public Library_CommitTagsToFileDelegate Library_CommitTagsToFile;
            public Library_GetLyricsDelegate Library_GetLyrics;
            public Library_GetArtworkDelegate Library_GetArtwork;
            public Library_QueryFilesDelegate Library_QueryFiles;
            public Library_QueryGetNextFileDelegate Library_QueryGetNextFile;
            public Player_GetPositionDelegate Player_GetPosition;
            public Player_SetPositionDelegate Player_SetPosition;
            public Player_GetPlayStateDelegate Player_GetPlayState;
            public Player_ActionDelegate Player_PlayPause;
            public Player_ActionDelegate Player_Stop;
            public Player_ActionDelegate Player_StopAfterCurrent;
            public Player_ActionDelegate Player_PlayPreviousTrack;
            public Player_ActionDelegate Player_PlayNextTrack;
            public Player_ActionDelegate Player_StartAutoDj;
            public Player_ActionDelegate Player_EndAutoDj;
            public Player_GetVolumeDelegate Player_GetVolume;
            public Player_SetVolumeDelegate Player_SetVolume;
            public Player_GetMuteDelegate Player_GetMute;
            public Player_SetMuteDelegate Player_SetMute;
            public Player_GetShuffleDelegate Player_GetShuffle;
            public Player_SetShuffleDelegate Player_SetShuffle;
            public Player_GetRepeatDelegate Player_GetRepeat;
            public Player_SetRepeatDelegate Player_SetRepeat;
            public Player_GetEqualiserEnabledDelegate Player_GetEqualiserEnabled;
            public Player_SetEqualiserEnabledDelegate Player_SetEqualiserEnabled;
            public Player_GetDspEnabledDelegate Player_GetDspEnabled;
            public Player_SetDspEnabledDelegate Player_SetDspEnabled;
            public Player_GetScrobbleEnabledDelegate Player_GetScrobbleEnabled;
            public Player_SetScrobbleEnabledDelegate Player_SetScrobbleEnabled;
            public NowPlaying_GetFileUrlDelegate NowPlaying_GetFileUrl;
            public NowPlaying_GetDurationDelegate NowPlaying_GetDuration;
            public NowPlaying_GetFilePropertyDelegate NowPlaying_GetFileProperty;
            public NowPlaying_GetFileTagDelegate NowPlaying_GetFileTag;
            public NowPlaying_GetLyricsDelegate NowPlaying_GetLyrics;
            public NowPlaying_GetArtworkDelegate NowPlaying_GetArtwork;
            public NowPlayingList_ActionDelegate NowPlayingList_Clear;
            public Library_QueryFilesDelegate NowPlayingList_QueryFiles;
            public Library_QueryGetNextFileDelegate NowPlayingList_QueryGetNextFile;
            public NowPlayingList_FileActionDelegate NowPlayingList_PlayNow;
            public NowPlayingList_FileActionDelegate NowPlayingList_QueueNext;
            public NowPlayingList_FileActionDelegate NowPlayingList_QueueLast;
            public NowPlayingList_ActionDelegate NowPlayingList_PlayLibraryShuffled;
            public Playlist_QueryPlaylistsDelegate Playlist_QueryPlaylists;
            public Playlist_QueryGetNextPlaylistDelegate Playlist_QueryGetNextPlaylist;
            public Playlist_GetTypeDelegate Playlist_GetType;
            public Library_QueryFilesDelegate Playlist_QueryFiles;
            public Library_QueryGetNextFileDelegate Playlist_QueryGetNextFile;
            public MB_WindowHandleDelegate MB_GetWindowHandle;
            public MB_RefreshPanelsDelegate MB_RefreshPanels;
            public MB_SendNotificationDelegate MB_SendNotification;
            public MB_AddMenuItemDelegate MB_AddMenuItem;
            public Setting_GetFieldNameDelegate Setting_GetFieldName;
            public Library_QueryGetAllFilesDelegate Library_QueryGetAllFiles;
            public Library_QueryGetAllFilesDelegate NowPlayingList_QueryGetAllFiles;
            public Library_QueryGetAllFilesDelegate Playlist_QueryGetAllFiles;
            public MB_CreateBackgroundTaskDelegate MB_CreateBackgroundTask;
            public MB_SetBackgroundTaskMessageDelegate MB_SetBackgroundTaskMessage;
        }

        public enum PluginType
        {
            Unknown = 0,
            General = 1,
            LyricsRetrieval = 2,
            ArtworkRetrieval = 3,
            PanelView = 4,
            DataStream = 5,
            InstantMessenger = 6,
            Storage = 7
        }

        [StructLayout(LayoutKind.Sequential)]
        public class PluginInfo
        {
            public short PluginInfoVersion;
            public PluginType Type;
            public string Name;
            public string Description;
            public string Author;
            public string TargetApplication;
            public short VersionMajor;
            public short VersionMinor;
            public short Revision;
            public short MinInterfaceVersion;
            public short MinApiRevision;
            public ReceiveNotificationFlags ReceiveNotifications;
            public int ConfigurationPanelHeight;
        }

        [Flags()]
        public enum ReceiveNotificationFlags
        {
            StartupOnly = 0x0,
            PlayerEvents = 0x1,
            DataStreamEvents = 0x2,
            TagEvents = 0x04
        }

        public enum NotificationType
        {
            PluginStartup = 0,          // notification sent after successful initialisation for an enabled plugin
            TrackChanged = 1,
            PlayStateChanged = 2,
            AutoDjStarted = 3,
            AutoDjStopped = 4,
            VolumeMuteChanged = 5,
            VolumeLevelChanged = 6,
            NowPlayingListChanged = 7,
            NowPlayingArtworkReady = 8,
            NowPlayingLyricsReady = 9,
            TagsChanging = 10,
            TagsChanged = 11,
            RatingChanged = 12,
            PlayCountersChanged = 13
        }

        public enum PluginCloseReason
        {
            MusicBeeClosing = 1,
            UserDisabled = 2
        }

        public enum CallbackType
        {
            SettingsUpdated = 1,
            StorageReady = 2,
            StorageFailed = 3,
            FilesRetrievedChanged = 4,
            FilesRetrievedNoChange = 5,
            FilesRetrievedFail = 6
        }

        public enum FilePropertyType
        {
            Url = 2,
            Kind = 4,
            Format = 5,
            Size = 7,
            Channels = 8,
            SampleRate = 9,
            Bitrate = 10,
            DateModified = 11,
            DateAdded = 12,
            LastPlayed = 13,
            PlayCount = 14,
            SkipCount = 15,
            Duration = 16,
            ReplayGainTrack = 94,
            ReplayGainAlbum = 95
        }

        public enum MetaDataType
        {
            TrackTitle = 65,
            Album = 30,
            AlbumArtist = 31,        // displayed album artist
            AlbumArtistRaw = 34,     // stored album artist
            Artist = 32,             // displayed artist
            MultiArtist = 33,        // individual artists, separated by a null char
            Artwork = 40,
            BeatsPerMin = 41,
            Composer = 43,           // displayed composer
            MultiComposer = 89,      // individual composers, separated by a null char
            Comment = 44,
            Conductor = 45,
            Custom1 = 46,
            Custom2 = 47,
            Custom3 = 48,
            Custom4 = 49,
            Custom5 = 50,
            Custom6 = 96,
            Custom7 = 97,
            Custom8 = 98,
            Custom9 = 99,
            DiscNo = 52,
            DiscCount = 54,
            Encoder = 55,
            Genre = 59,
            GenreCategory = 60,
            Grouping = 61,
            Keywords = 84,
            HasLyrics = 63,
            Lyricist = 62,
            Mood = 64,
            Occasion = 66,
            Origin = 67,
            Publisher = 73,
            Quality = 74,
            Rating = 75,
            RatingLove = 76,
            RatingAlbum = 104,
            Tempo = 85,
            TrackNo = 86,
            TrackCount = 87,
            Virtual1 = 109,
            Virtual2 = 110,
            Virtual3 = 111,
            Year = 88
        }

        public enum LyricsType
        {
            NotSpecified = 0,
            Synchronised = 1,
            UnSynchronised = 2
        }

        public enum PlayState
        {
            Undefined = 0,
            Loading = 1,
            Playing = 3,
            Paused = 6,
            Stopped = 7
        }

        public enum RepeatMode
        {
            None = 0,
            All = 1,
            One = 2
        }

        public enum PlaylistFormat
        {
            Unknown = 0,
            M3u = 1,
            Xspf = 2,
            Asx = 3,
            Wpl = 4,
            Pls = 5,
            Auto = 7,
            M3uAscii = 8
        }

        public enum SkinElement
        {
            SkinInputControl = 7,
            SkinInputPanel = 10,
            SkinInputPanelLabel = 14
        }

        public enum ElementState
        {
            ElementStateDefault = 0,
            ElementStateModified = 6
        }

        public enum ElementComponent
        {
            ComponentBorder = 0,
            ComponentBackground = 1,
            ComponentForeground = 3
        }

        public delegate void MB_ReleaseStringDelegate(string p1);
        public delegate void MB_TraceDelegate(string p1);
        public delegate IntPtr MB_WindowHandleDelegate();
        public delegate void MB_RefreshPanelsDelegate();
        public delegate void MB_SendNotificationDelegate(CallbackType type);
        public delegate void MB_AddMenuItemDelegate(string menuPath, string hotkeyDescription, EventHandler handler);
        public delegate void MB_CreateBackgroundTaskDelegate(System.Threading.ThreadStart taskCallback, System.Windows.Forms.Form owner);
        public delegate void MB_SetBackgroundTaskMessageDelegate(string message);
        public delegate string Setting_GetFieldNameDelegate(MetaDataType type);
        public delegate string Setting_GetPersistentStoragePathDelegate();
        public delegate string Setting_GetSkinDelegate();
        public delegate int Setting_GetSkinElementColourDelegate(SkinElement element, ElementState state, ElementComponent component);
        public delegate bool Setting_IsWindowBordersSkinnedDelegate();
        public delegate string Library_GetFilePropertyDelegate(string sourceFileUrl, FilePropertyType type);
        public delegate string Library_GetFileTagDelegate(string sourceFileUrl, MetaDataType type);
        public delegate bool Library_SetFileTagDelegate(string sourceFileUrl, MetaDataType type, string value);
        public delegate bool Library_CommitTagsToFileDelegate(string sourceFileUrl);
        public delegate string Library_GetLyricsDelegate(string sourceFileUrl, int type);
        public delegate string Library_GetArtworkDelegate(string sourceFileUrl, int index);
        public delegate bool Library_QueryFilesDelegate(string query);
        public delegate string Library_QueryGetNextFileDelegate();
        public delegate string Library_QueryGetAllFilesDelegate();
        public delegate int Player_GetPositionDelegate();
        public delegate bool Player_SetPositionDelegate(int position);
        public delegate PlayState Player_GetPlayStateDelegate();
        public delegate bool Player_ActionDelegate();
        public delegate float Player_GetVolumeDelegate();
        public delegate bool Player_SetVolumeDelegate(float volume);
        public delegate bool Player_GetMuteDelegate();
        public delegate bool Player_SetMuteDelegate(bool mute);
        public delegate bool Player_GetShuffleDelegate();
        public delegate bool Player_SetShuffleDelegate(bool shuffle);
        public delegate RepeatMode Player_GetRepeatDelegate();
        public delegate bool Player_SetRepeatDelegate(RepeatMode repeat);
        public delegate bool Player_GetEqualiserEnabledDelegate();
        public delegate bool Player_SetEqualiserEnabledDelegate(bool shuffle);
        public delegate bool Player_GetDspEnabledDelegate();
        public delegate bool Player_SetDspEnabledDelegate(bool shuffle);
        public delegate bool Player_GetScrobbleEnabledDelegate();
        public delegate bool Player_SetScrobbleEnabledDelegate(bool shuffle);
        public delegate string NowPlaying_GetFileUrlDelegate();
        public delegate int NowPlaying_GetDurationDelegate();
        public delegate string NowPlaying_GetFilePropertyDelegate(FilePropertyType type);
        public delegate string NowPlaying_GetFileTagDelegate(MetaDataType type);
        public delegate string NowPlaying_GetLyricsDelegate();
        public delegate string NowPlaying_GetArtworkDelegate();
        public delegate bool NowPlayingList_ActionDelegate();
        public delegate bool NowPlayingList_FileActionDelegate(string sourceFileUrl);
        public delegate bool Playlist_QueryPlaylistsDelegate();
        public delegate string Playlist_QueryGetNextPlaylistDelegate();
        public delegate PlaylistFormat Playlist_GetTypeDelegate(string playlistUrl);
    }
}