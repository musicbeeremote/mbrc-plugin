using System;
using System.Collections.Generic;
using mbrcPartyMode;
using MusicBeePlugin.AndroidRemote.Commands.Internal;
using MusicBeePlugin.AndroidRemote.Commands.Requests;
using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.PartyMode
{
    public static class PartyModeCommandMapper
    {
        private static readonly Dictionary<Type, MappingCommand> CommandMap = new Dictionary<Type, MappingCommand>
        {
            {typeof(RequestStop), MappingCommand.StopPlayer},
            {typeof(RequestPlay), MappingCommand.StartPlayer},
            {typeof(RequestPlaylistPlay), MappingCommand.StartPlayer},
            {typeof(RequestNowPlayingPlay), MappingCommand.StartPlayer},
            {typeof(RequestNowplayingQueue), MappingCommand.StartPlayer},
            {typeof(RequestNowPlayingSearch), MappingCommand.StartPlayer},
            {typeof(RequestNowPlayingTrackRemoval), MappingCommand.StartPlayer},
            {typeof(RequestNextTrack), MappingCommand.SkipForward},
            {typeof(RequestPreviousTrack), MappingCommand.SkipBackward},
            {typeof(RequestPlayPause), MappingCommand.StopPlayer},
            {typeof(RequestVolume), MappingCommand.CanSetVolume},
            {typeof(ClientConnected), MappingCommand.ClientConnected},
            {typeof(ClientDisconnected), MappingCommand.ClientDisconnected},
            {typeof(StopSocketServer), MappingCommand.StopServer}
        };

        public static MappingCommand MapCommand(ICommand cmd)
        {
            MappingCommand command;

            return CommandMap.TryGetValue(cmd.GetType(), out command)
                ? command
                : MappingCommand.CommandNotImplemented;
        }
    }
}