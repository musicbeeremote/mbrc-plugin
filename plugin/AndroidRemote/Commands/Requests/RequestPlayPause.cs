﻿using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestPlayPause : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPlayPauseTrack(eEvent.ClientId);
        }
    }
}