using mbrcPartyMode;
using mbrcPartyMode.Tools;
using MusicBeePlugin.AndroidRemote.Commands.Internal;
using MusicBeePlugin.AndroidRemote.Commands.Requests;
using MusicBeePlugin.AndroidRemote.Interfaces;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace MusicBeePlugin.AndroidRemote.Commands
{
    public static class PartyModeCommandMapper
    {
        public static MappingCommand MapCommand(ICommand cmd)
        {
            ICommand stopPlayer = cmd as RequestStop;
            if (stopPlayer != null) return MappingCommand.StopPlayer;

            #region play

            ICommand startPlayer = cmd as RequestPlay;
            if (startPlayer != null) return MappingCommand.StartPlayer;

            ICommand requestPlaylistPlay = cmd as RequestPlaylistPlay;
            if (requestPlaylistPlay != null) return MappingCommand.StartPlayer;

            ICommand requestNowPlayingPlay = cmd as RequestNowPlayingPlay;
            if (requestNowPlayingPlay != null) return MappingCommand.StartPlayer;

            ICommand requestNowplayingQueue = cmd as RequestNowplayingQueue;
            if (requestNowplayingQueue != null) return MappingCommand.StartPlayer;

            ICommand requestNowPlayingSearch = cmd as RequestNowPlayingSearch;
            if (requestNowPlayingSearch != null) return MappingCommand.StartPlayer;

            ICommand requestNowPlayingTrackRemoval = cmd as RequestNowPlayingTrackRemoval;
            if (requestNowPlayingTrackRemoval != null) return MappingCommand.StartPlayer;

            #endregion play

            #region skip forward

            ICommand requestNextTrack = cmd as RequestNextTrack;
            if (requestNextTrack != null) return MappingCommand.SkipForward;

            #endregion
            
            #region skip backward

            ICommand requestPreviousTrack = cmd as RequestPreviousTrack;
            if (requestPreviousTrack != null) return MappingCommand.SkipBackward;
            #endregion
            
            #region stop

            ICommand pausePlayer = cmd as RequestPlayPause;
            if (pausePlayer != null) return MappingCommand.StopPlayer;

            #endregion stop

            #region volume

            ICommand requestVolumne = cmd as RequestVolume;
            if (requestVolumne != null) return MappingCommand.CanSetVolume;

            #endregion

            #region internal

            ICommand clientConnected = cmd as ClientConnected;
            if (clientConnected != null) return MappingCommand.ClientConnected;

            ICommand clientDisConnected = cmd as ClientDisconnected;
            if (clientDisConnected != null) return MappingCommand.ClientDisconnected;

            ICommand stopSocketServer = cmd as StopSocketServer;
            if (stopSocketServer != null) return MappingCommand.StopServer;

            #endregion internal

            return MappingCommand.CommandNotImplemented;
        }
    }
}