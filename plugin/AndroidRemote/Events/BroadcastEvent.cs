using System.Collections.Generic;
using System.Linq;
using MusicBeePlugin.AndroidRemote.Entities;

namespace MusicBeePlugin.AndroidRemote.Networking
{
    public class BroadcastEvent
    {
        public BroadcastEvent()
        {
            BroadcastMessages = new Dictionary<int, SocketMessage>();
        }

        private Dictionary<int, SocketMessage> BroadcastMessages { get; }


        public void AddMessage(int version, SocketMessage message)
        {
            BroadcastMessages.Add(version, message);
        }

        public string GetMessage(int clientVersion)
        {
            var apiVersions = BroadcastMessages.Keys.OrderBy(d => d);
            var messageApi = 2;
            foreach (var version in apiVersions)
            {
                if (clientVersion >= version)
                {
                    messageApi = version;
                }
                else
                {
                    break;
                }
            }
            SocketMessage message;
            var retrieved = BroadcastMessages.TryGetValue(messageApi, out message);
            return retrieved ? message.ToJsonString() : string.Empty;
        }
    }
}