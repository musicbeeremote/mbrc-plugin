using System;
using System.Collections.Generic;
using MusicBeeRemoteCore.Core.Support;
using MusicBeeRemoteCore.Remote.Commands.Internal;
using MusicBeeRemoteCore.Remote.Enumerations;
using MusicBeeRemoteCore.Remote.Interfaces;
using MusicBeeRemoteCore.Remote.Model.Entities;
using MusicBeeRemoteCore.Remote.Networking;
using NLog;
using TinyMessenger;

namespace MusicBeeRemoteCore.Remote.Commands.Requests
{
    internal class RequestLibSearchGenre : ICommand
    {
        public void Execute(IEvent @event)
        {
            Plugin.Instance.LibrarySearchGenres(@event.Data.ToString(), @event.ConnectionId);
        }
    }

    internal class RequestLibSearchArtist : ICommand
    {
        public void Execute(IEvent @event)
        {
            Plugin.Instance.LibrarySearchArtist(@event.Data.ToString(), @event.ConnectionId);
        }
    }

    internal class RequestLibSearchAlbum : ICommand
    {
        public void Execute(IEvent @event)
        {
            Plugin.Instance.LibrarySearchAlbums(@event.Data.ToString(), @event.ConnectionId);
        }
    }

    internal class RequestLibQueueTrack : ICommand
    {
        private readonly ISearchQueue _searchQueue;

        public RequestLibQueueTrack(ISearchQueue searchQueue)
        {
            _searchQueue = searchQueue;
        }

        public void Execute(IEvent @event)
        {
            string type, query;

            ((Dictionary<string, string>) @event.Data).TryGetValue("type", out type);
            ((Dictionary<string, string>) @event.Data).TryGetValue("query", out query);
            QueueType qType;
            switch (type)
            {
                case "next":
                    qType = QueueType.Next;
                    break;
                case "last":
                    qType = QueueType.Last;
                    break;
                case "now":
                    qType = QueueType.PlayNow;
                    break;
                default:
                    qType = QueueType.Next;
                    break;
            }

            _searchQueue.RequestQueueFiles(qType, MetaTag.title, query);
        }
    }

    internal class RequestLibQueueGenre : ICommand
    {
        private readonly ISearchQueue _searchQueue;

        public RequestLibQueueGenre(ISearchQueue searchQueue)
        {
            _searchQueue = searchQueue;
        }

        public void Execute(IEvent @event)
        {
            string type, query;

            ((Dictionary<string, string>) @event.Data).TryGetValue("type", out type);
            ((Dictionary<string, string>) @event.Data).TryGetValue("query", out query);
            QueueType qType;
            switch (type)
            {
                case "next":
                    qType = QueueType.Next;
                    break;
                case "last":
                    qType = QueueType.Last;
                    break;
                case "now":
                    qType = QueueType.PlayNow;
                    break;
                default:
                    qType = QueueType.Next;
                    break;
            }
            _searchQueue.RequestQueueFiles(qType, MetaTag.genre, query);
        }
    }

    internal class RequestLibQueueArtist : ICommand
    {
        private readonly ISearchQueue _searchQueue;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public RequestLibQueueArtist(ISearchQueue searchQueue)
        {
            _searchQueue = searchQueue;
        }

        public void Execute(IEvent @event)
        {
            try
            {
                string type, query;

                ((Dictionary<string, string>) @event.Data).TryGetValue("type", out type);
                ((Dictionary<string, string>) @event.Data).TryGetValue("query", out query);

                QueueType qType;
                switch (type)
                {
                    case "next":
                        qType = QueueType.Next;
                        break;
                    case "last":
                        qType = QueueType.Last;
                        break;
                    case "now":
                        qType = QueueType.PlayNow;
                        break;
                    default:
                        qType = QueueType.Next;
                        break;
                }

                _searchQueue.RequestQueueFiles(qType, MetaTag.artist, query);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to queue artist");
            }
        }
    }

    internal class RequestLibQueueAlbum : ICommand
    {
        private readonly ISearchQueue _searchQueue;

        public RequestLibQueueAlbum(ISearchQueue searchQueue)
        {
            _searchQueue = searchQueue;
        }

        public void Execute(IEvent @event)
        {
            string type, query;

            ((Dictionary<string, string>) @event.Data).TryGetValue("type", out type);
            ((Dictionary<string, string>) @event.Data).TryGetValue("query", out query);
            QueueType qType;
            switch (type)
            {
                case "next":
                    qType = QueueType.Next;
                    break;
                case "last":
                    qType = QueueType.Last;
                    break;
                case "now":
                    qType = QueueType.PlayNow;
                    break;
                case "add-all":
                    qType = QueueType.AddAndPlay;
                    break;
                default:
                    qType = QueueType.Next;
                    break;
            }
            _searchQueue.RequestQueueFiles(qType, MetaTag.album, query);
        }
    }

    internal class RequestLibGenreArtists : ICommand
    {
        public void Execute(IEvent @event)
        {
            Plugin.Instance.LibraryGetGenreArtists(@event.DataToString(), @event.ConnectionId);
        }
    }

    internal class RequestLibArtistAlbums : ICommand
    {
        public void Execute(IEvent @event)
        {
            Plugin.Instance.LibraryGetArtistAlbums(@event.DataToString(), @event.ConnectionId);
        }
    }

    internal class RequestLibAlbumTracks : ICommand
    {
        public void Execute(IEvent @event)
        {
            Plugin.Instance.LibraryGetAlbumTracks(@event.DataToString(), @event.ConnectionId);
        }
    }

    internal class RequestLibSearchTitle : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ISearchApi _searchApi;

        public RequestLibSearchTitle(ITinyMessengerHub hub, ISearchApi searchApi)
        {
            _hub = hub;
            _searchApi = searchApi;
        }

        public void Execute(IEvent @event)
        {
            var searchResults = _searchApi.LibrarySearchTitle(@event.DataToString());
            var message = new SocketMessage(Constants.LibrarySearchTitle, searchResults);
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }
}