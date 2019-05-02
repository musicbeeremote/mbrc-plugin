using Moq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Commands.Requests;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Model.Entities;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TinyMessenger;

namespace MusicBeeRemote.Test.Core.Commands.Requests
{
    [TestFixture]
    public class RequestNowPlayingPlayTest
    {
        private RequestNowPlayingPlay _request;
        private Mock<INowPlayingApiAdapter> _apiAdapter;
        private Mock<ITinyMessengerHub> _hub;

        [SetUp]
        public void Before()
        {
            _apiAdapter = new Mock<INowPlayingApiAdapter>();
            _hub = new Mock<ITinyMessengerHub>();
            _request = new RequestNowPlayingPlay(_hub.Object, _apiAdapter.Object);
        }

        [Test(Description = "When receiving a path in the nowplayinglistplay should call PlayPath")]
        public void NowPlayingPlayFromPath()
        {
            const string message = @" 
                {
                    ""context"": ""nowplayinglistplay"",
                    ""data"": ""C:\\path\\song.mp3""
                } 
            ";
            var socketMessage = new SocketMessage(JObject.Parse(message));
            var messageEvent = new MessageEvent(
                socketMessage.Context,
                socketMessage.Data,
                "",
                "5"
            );
            _request.Execute(messageEvent);
            _apiAdapter.Verify(adapter => adapter.PlayPath("C:\\path\\song.mp3"));
            var captor = new ArgumentCaptor<PluginResponseAvailableEvent>();
            _hub.Verify(hub => hub.Publish(captor.Capture()));
            Assert.AreEqual("nowplayinglistplay", captor.Value.Message.Context);
        }

        [Test(Description = "When receiving a number in the nowplayinglistplay should call PlayIndex")]
        public void NowPlayingPlayFromIndex()
        {
            const string message = @" 
                {
                    ""context"": ""nowplayinglistplay"",
                    ""data"": 2
                } 
            ";
            var socketMessage = new SocketMessage(JObject.Parse(message));
            var messageEvent = new MessageEvent(
                socketMessage.Context,
                socketMessage.Data,
                "",
                "5"
            );
            _request.Execute(messageEvent);
            _apiAdapter.Verify(adapter => adapter.PlayIndex(2));
            var captor = new ArgumentCaptor<PluginResponseAvailableEvent>();
            _hub.Verify(hub => hub.Publish(captor.Capture()));
            Assert.AreEqual("nowplayinglistplay", captor.Value.Message.Context);
        }
    }
}