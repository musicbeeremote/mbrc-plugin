using MusicBeeRemote.Core.Network;

namespace MusicBeeRemote.Core.Model.Entities
{
    public static class SocketMessageExtesion
    {
        public static bool IsPlayer(this SocketMessage message)
        {
            return message.Context == Constants.Player;
        }

        public static bool IsProtocol(this SocketMessage message)
        {
            return message.Context == Constants.Protocol;
        }

        public static bool IsVerifyConnection(this SocketMessage message)
        {
            return message.Context == Constants.VerifyConnection;
        }
    }
}