using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace MusicBeePlugin.Models.Entities
{
    [DataContract]
    public class SocketMessage
    {
        /// <summary>
        ///     Shared serializer settings for consistent JSON output.
        ///     Includes StringEnumConverter to serialize enums as strings.
        ///     NullValueHandling.Ignore matches original ServiceStack.Text behavior.
        /// </summary>
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
            NullValueHandling = NullValueHandling.Ignore
        };

        public SocketMessage(string context, object data)
        {
            Context = context;
            Data = data;
        }

        public SocketMessage(JObject jObj)
        {
            Context = jObj["context"]?.ToString() ?? string.Empty;

            var dataToken = jObj["data"];
            if (dataToken == null)
                Data = string.Empty;
            else if (dataToken.Type == JTokenType.Object)
                Data = dataToken;
            else
                Data = dataToken.ToString();
        }

        public SocketMessage()
        {
        }

        [DataMember(Name = "context")] public string Context { get; set; }

        [DataMember(Name = "data")] public object Data { get; set; }

        public string ToJsonString()
        {
            return JsonConvert.SerializeObject(this, SerializerSettings);
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, SerializerSettings);
        }
    }
}
