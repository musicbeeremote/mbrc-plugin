using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MusicBeePlugin.Events.Contracts;
using MusicBeePlugin.Protocol.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MusicBeeRemote.Core.Tests.Mocks
{
    /// <summary>
    /// Mock event aggregator for integration testing.
    /// Captures published messages for verification.
    /// </summary>
    public class MockEventAggregator : IEventAggregator
    {
        public List<object> PublishedMessages { get; } = new List<object>();
        public List<object> AsyncPublishedMessages { get; } = new List<object>();

        public void Publish<T>(T eventObj) where T : class
        {
            PublishedMessages.Add(eventObj);
        }

        public Task PublishAsync<T>(T eventObj) where T : class
        {
            AsyncPublishedMessages.Add(eventObj);
            return Task.CompletedTask;
        }

        public IDisposable Subscribe<T>(Action<T> handler) where T : class
        {
            return new MockDisposable();
        }

        public IDisposable Subscribe<T>(Func<T, Task> handler) where T : class
        {
            return new MockDisposable();
        }

        public void Clear()
        {
            PublishedMessages.Clear();
            AsyncPublishedMessages.Clear();
        }

        /// <summary>
        /// Get all published MessageSendEvent objects.
        /// </summary>
        public IEnumerable<MessageSendEvent> GetMessageSendEvents()
        {
            return PublishedMessages.OfType<MessageSendEvent>()
                .Concat(AsyncPublishedMessages.OfType<MessageSendEvent>());
        }

        /// <summary>
        /// Get the serialized JSON messages from all MessageSendEvent objects.
        /// </summary>
        public IEnumerable<string> GetSerializedMessages()
        {
            return GetMessageSendEvents().Select(e => e.Message);
        }

        /// <summary>
        /// Get a MessageSendEvent by context (command type).
        /// </summary>
        public MessageSendEvent GetMessageByContext(string context)
        {
            return GetMessageSendEvents()
                .FirstOrDefault(e => ParseContext(e.Message) == context);
        }

        /// <summary>
        /// Verify a response was sent with specific context and optional data validation.
        /// </summary>
        public bool HasResponse(string context)
        {
            return GetMessageSendEvents().Any(e => ParseContext(e.Message) == context);
        }

        /// <summary>
        /// Get all response messages for a specific context.
        /// </summary>
        public IEnumerable<MessageSendEvent> GetResponsesByContext(string context)
        {
            return GetMessageSendEvents().Where(e => ParseContext(e.Message) == context);
        }

        /// <summary>
        /// Parse a serialized message and extract the context field.
        /// </summary>
        public static string ParseContext(string jsonMessage)
        {
            try
            {
                var json = JObject.Parse(jsonMessage);
                return json["context"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Parse a serialized message and extract the data field.
        /// </summary>
        public static JToken ParseData(string jsonMessage)
        {
            try
            {
                var json = JObject.Parse(jsonMessage);
                return json["data"];
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Parse a serialized message and extract the data as a typed object.
        /// </summary>
        public static T ParseData<T>(string jsonMessage)
        {
            var data = ParseData(jsonMessage);
            if (data == null)
                return default;

            try
            {
                return data.ToObject<T>();
            }
            catch
            {
                return default;
            }
        }

        /// <summary>
        /// Get the first MessageSendEvent and parse its response as a typed object.
        /// </summary>
        public T GetFirstResponseData<T>(string context)
        {
            var message = GetMessageByContext(context);
            if (message == null)
                return default;
            return ParseData<T>(message.Message);
        }

        /// <summary>
        /// Get the parsed JSON object of the first response for a given context.
        /// </summary>
        public JObject GetFirstResponseJson(string context)
        {
            var message = GetMessageByContext(context);
            if (message == null)
                return null;

            try
            {
                return JObject.Parse(message.Message);
            }
            catch
            {
                return null;
            }
        }

        private sealed class MockDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
