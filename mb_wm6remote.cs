using System;
using System.Runtime.InteropServices;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private MusicBeeApiInterface mbApiInterface;
        private PluginInfo about = new PluginInfo();
        SocketServer mbSoc;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            mbApiInterface = (MusicBeeApiInterface)Marshal.PtrToStructure(apiInterfacePtr, typeof(MusicBeeApiInterface));
            about.PluginInfoVersion = PluginInfoVersion;
            about.Name = "MusicBee Remote Control";
            about.Description = "A plugin to allow music bee remote controll through mobile applications and network.";
            about.Author = "Kelsos";
            about.TargetApplication = "MusicBee Remote";   // current only applies to artwork, lyrics or instant messenger name that appears in the provider drop down selector or target Instant Messenger
            about.Type = PluginType.General;
            about.VersionMajor = 1;  // your plugin version
            about.VersionMinor = 0;
            about.Revision = 1;
            about.MinInterfaceVersion = MinInterfaceVersion;
            about.MinApiRevision = MinApiRevision;
            about.ReceiveNotifications = ReceiveNotificationFlags.PlayerEvents;
            about.ConfigurationPanelHeight = 10;   // not implemented yet: height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function
           mbSoc = new SocketServer();
            return about;
        }

        public bool Configure(IntPtr panelHandle)
        {
            // save any persistent settings in a sub-folder of this path
            string dataPath = mbApiInterface.Setting_GetPersistentStoragePath();
            return false;
        }

        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        public void Close(PluginCloseReason reason)
        {
            mbSoc.continueListening = false;
        }

        // uninstall this plugin - clean up any persisted files
        public void Uninstall()
        {
        }

        // receive event notifications from MusicBee
        // only required if about.ReceiveNotificationFlags = PlayerEvents
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            // perform some action depending on the notification type
            switch (type)
            {
                case NotificationType.PluginStartup:
                    // perform startup initialisation
                    switch (mbApiInterface.Player_GetPlayState())
                    {
                        case PlayState.Playing:
                        case PlayState.Paused:
                            // ...
                            break;
                    }
                    break;
                case NotificationType.TrackChanged:
                     mbSoc.artist = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist);
                     mbSoc.song = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle);
                     mbSoc.album = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Album);
                     mbSoc.year = mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Year);
                     mbSoc.imageData = mbApiInterface.NowPlaying_GetArtwork();
                     mbSoc.songChanged = true;

                    // ...
                    break;
            }
        }

        // return lyrics for the requested artist/title
        // only required if PluginType = LyricsRetrieval
        // return null if no lyrics are found
        public string RetrieveLyrics(string sourceFileUrl, string artist, string trackTitle, string album, bool synchronisedPreferred)
        {
            return null;
        }

        // return Base64 string representation of the artwork binary data
        // only required if PluginType = ArtworkRetrieval
        // return null if no artwork is found
        public string RetrieveArtwork(string sourceFileUrl, string albumArtist, string album)
        {
            //Return Convert.ToBase64String(artworkBinaryData)
            return null;
        }
    }
}