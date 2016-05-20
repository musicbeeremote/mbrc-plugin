using System.Runtime.Serialization;
using ServiceStack.Text;

namespace MusicBeePlugin.AndroidRemote.Entities
{
    [DataContract]
    public class SocketMessage
    {
        public SocketMessage(string context, object data)
        {
            Context = context;
            Data = data;
        }

        public SocketMessage(JsonObject jObj)
        {
            Context = jObj.Get("context");

            var messageData = jObj.Get("data");
            if (messageData == null)
            {
                Data = "";
            }
            else
            {
                if (messageData.Contains("{") && messageData.Contains("}"))
                {
                    Data = jObj.Object("data");
                }
                else
                {
                    Data = messageData;
                }
            }
        }

        public SocketMessage()
        {
            
        }

        [DataMember(Name = "context")]
        public string Context { get; set; }

        [DataMember(Name = "data")]
        public object Data { get; set; }

        public string ToJsonString()
        {
            return JsonSerializer.SerializeToString(this);
        }
    }
}