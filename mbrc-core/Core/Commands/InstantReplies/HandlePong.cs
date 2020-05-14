using System;
using MusicBeeRemote.Core.Events;
using NLog;

namespace MusicBeeRemote.Core.Commands.InstantReplies
{
    public sealed class HandlePong : ICommand
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public void Execute(IEvent receivedEvent)
        {
            _logger.Debug($"Pong: {DateTime.UtcNow}");
        }
    }
}
