using System;
using System.Collections.Generic;
using MbrcPartyMode;
using MusicBeePlugin.AndroidRemote.Commands.Internal;
using MusicBeePlugin.AndroidRemote.Commands.Requests;
using MusicBeePlugin.AndroidRemote.Interfaces;
using static MbrcPartyMode.MappingCommand;

namespace MusicBeePlugin.PartyMode
{
    public static class PartyModeCommandMapper
    {
        private static readonly Dictionary<Type, MappingCommand> CommandMap = new Dictionary<Type, MappingCommand>
        {
            {typeof(RequestStop), StopPlayer},
            {typeof(RequestPlay), StartPlayer},
            {typeof(RequestPlaylistPlay), StartPlayer},
            {typeof(RequestNowPlayingPlay), StartPlayer},
            {typeof(RequestNowplayingQueue), StartPlayer},
            {typeof(RequestNowPlayingSearch), StartPlayer},
            {typeof(RequestNowPlayingTrackRemoval), StartPlayer},
            {typeof(RequestNextTrack), SkipForward},
            {typeof(RequestPreviousTrack), SkipBackward},
            {typeof(RequestPlayPause), StopPlayer},
            {typeof(RequestVolume), CanSetVolume},
            {typeof(ClientConnected), MappingCommand.ClientConnected},
            {typeof(ClientDisconnected), MappingCommand.ClientDisconnected},
            {typeof(StopSocketServer), StopServer}
        };

        public static MappingCommand MapCommand(ICommand cmd)
        {
            MappingCommand command;

            return CommandMap.TryGetValue(cmd.GetType(), out command)
                ? command
                : CommandNotImplemented;
        }
    }
}