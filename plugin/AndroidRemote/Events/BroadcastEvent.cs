using System.Collections.Generic;
using System.Linq;
using MusicBeePlugin.AndroidRemote.Model.Entities;

namespace MusicBeePlugin.AndroidRemote.Events
{
    public class BroadcastEvent
    {
        private readonly string _content;

        public BroadcastEvent(string content)
        {
            _content = content;
            BroadcastMessages = new Dictionary<int, SocketMessage>();
        }

        private Dictionary<int, SocketMessage> BroadcastMessages { get; }

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

        public void AddPayload(int apiVersion, object payload)
        {
            var socketMessage = new SocketMessage(_content, payload);
            BroadcastMessages.Add(apiVersion, socketMessage);
        }

        public override string ToString()
        {
            var messages = string.Join(";", BroadcastMessages.Select(x => x.Key + "=" + x.Value));
            return $"{nameof(BroadcastMessages)}: {messages}, {nameof(_content)}: {_content}";
        }
    }
}