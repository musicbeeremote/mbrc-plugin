using System;
using System.Runtime.InteropServices;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private MusicBeeApiInterface _mbApiInterface;
        private readonly PluginInfo _about = new PluginInfo();
        private SocketServer _mbSoc;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            _mbApiInterface = (MusicBeeApiInterface)Marshal.PtrToStructure(apiInterfacePtr, typeof(MusicBeeApiInterface));
            _about.PluginInfoVersion = PluginInfoVersion;
            _about.Name = "MusicBee Remote Control";
            _about.Description = "A plugin to allow music bee remote controll through mobile applications and network.";
            _about.Author = "Kelsos";
            _about.TargetApplication = "MusicBee Remote";   // current only applies to artwork, lyrics or instant messenger name that appears in the provider drop down selector or target Instant Messenger
            _about.Type = PluginType.General;
            _about.VersionMajor = 1;  // your plugin version
            _about.VersionMinor = 0;
            _about.Revision = 1;
            _about.MinInterfaceVersion = MinInterfaceVersion;
            _about.MinApiRevision = MinApiRevision;
            _about.ReceiveNotifications = ReceiveNotificationFlags.PlayerEvents;
            _about.ConfigurationPanelHeight = 10;   // not implemented yet: height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function
            _mbSoc = new SocketServer(this);
            SocketServer.Start();
            return _about;
        }

        public bool Configure(IntPtr panelHandle)
        {
            // save any persistent settings in a sub-folder of this path
            var dataPath = _mbApiInterface.Setting_GetPersistentStoragePath();
            return false;
        }

        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        public void Close(PluginCloseReason reason)
        {
            SocketServer.Stop();
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
                    switch (_mbApiInterface.Player_GetPlayState())
                    {
                        case PlayState.Playing:
                        case PlayState.Paused:
                            // ...
                            break;
                    }
                    break;
                case NotificationType.TrackChanged:


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
        public void PlayNextTrack()
        {
            _mbApiInterface.Player_PlayNextTrack();
        }

        public void PlayPauseTrack()
        {
            _mbApiInterface.Player_PlayPause();
        }
        public void PlayPreviousTrack()
        {
            _mbApiInterface.Player_PlayPreviousTrack();
        }

        public int IncreaseVolume()
        {
            float vol = (float)Math.Round(_mbApiInterface.Player_GetVolume() + (float)0.1, 1);
            _mbApiInterface.Player_SetVolume(vol);
            return (int)Math.Round(_mbApiInterface.Player_GetVolume() * 10, 1);
            
        }
        public int DecreaseVolume()
        {
            float vol = (float)Math.Round(_mbApiInterface.Player_GetVolume() - (float)0.1, 1);
            _mbApiInterface.Player_SetVolume(vol);
            return (int)Math.Round(_mbApiInterface.Player_GetVolume() * 10, 1);
        }
    }
}