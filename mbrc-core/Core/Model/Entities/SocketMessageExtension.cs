using System;
using MusicBeeRemote.Core.Network;

namespace MusicBeeRemote.Core.Model.Entities
{
    public static class SocketMessageExtension
    {
        public static bool IsPlayer(this SocketMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            return message.Context == Constants.Player;
        }

        public static bool IsProtocol(this SocketMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            return message.Context == Constants.Protocol;
        }

        public static bool IsVerifyConnection(this SocketMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            return message.Context == Constants.VerifyConnection;
        }
    }
}
