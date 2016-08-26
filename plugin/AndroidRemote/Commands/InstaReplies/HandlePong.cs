using System;
using System.Diagnostics;
using MusicBeePlugin.AndroidRemote.Interfaces;
using NLog;

namespace MusicBeePlugin.AndroidRemote.Commands.InstaReplies
{
    internal class HandlePong : ICommand
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public void Execute(IEvent eEvent)
        {
            _logger.Debug($"Pong: {DateTime.UtcNow}");
        }
    }
}