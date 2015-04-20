using System;
using System.Diagnostics;
using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.InstaReplies
{
    class HandlePong : ICommand
    {
        public void Execute(IEvent eEvent)
        {
#if DEBUG
            Debug.WriteLine("Pong: {0}", DateTime.UtcNow);
#endif
        }
    }
}
