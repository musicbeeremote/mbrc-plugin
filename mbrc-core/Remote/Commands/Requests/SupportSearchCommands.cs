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
        private readonly ITinyMessengerHub _hub;
        private readonly ISearchApi _searchApi;

        public RequestLibSearchGenre(ITinyMessengerHub hub, ISearchApi searchApi)
        {
            _hub = hub;
            _searchApi = searchApi;
        }

        public void Execute(IEvent @event)
        {
            var results = _searchApi.LibrarySearchGenres(@event.Data.ToString());
            var message = new SocketMessage(Constants.LibrarySearchGenre, results);
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }

    internal class RequestLibSearchArtist : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ISearchApi _searchApi;

        public RequestLibSearchArtist(ITinyMessengerHub hub, ISearchApi searchApi)
        {
            _hub = hub;
            _searchApi = searchApi;
        }

        public void Execute(IEvent @event)
        {
            var result = _searchApi.LibrarySearchArtist(@event.Data.ToString());
            var message = new SocketMessage(Constants.LibrarySearchArtist, result);
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }

    internal class RequestLibSearchAlbum : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ISearchApi _searchApi;

        public RequestLibSearchAlbum(ITinyMessengerHub hub, ISearchApi searchApi)
        {
            _hub = hub;
            _searchApi = searchApi;
        }

        public void Execute(IEvent @event)
        {
            var results = _searchApi.LibrarySearchAlbums(@event.Data.ToString());
            var message = new SocketMessage(Constants.LibrarySearchAlbum, results);
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }

    internal class RequestLibQueueTrack : LimitedCommand
    {
        private readonly ISearchQueue _searchQueue;

        public RequestLibQueueTrack(ISearchQueue searchQueue)
        {
            _searchQueue = searchQueue;
        }

        public override void Execute(IEvent @event)
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

        public override CommandPermissions GetPermissions() => CommandPermissions.AddTrack |
                                                               CommandPermissions.StartPlayback;
    }

    internal class RequestLibQueueGenre : LimitedCommand
    {
        private readonly ISearchQueue _searchQueue;

        public RequestLibQueueGenre(ISearchQueue searchQueue)
        {
            _searchQueue = searchQueue;
        }

        public override void Execute(IEvent @event)
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

        public override CommandPermissions GetPermissions() => CommandPermissions.AddTrack |
                                                               CommandPermissions.StartPlayback;
    }

    internal class RequestLibQueueArtist : LimitedCommand
    {
        private readonly ISearchQueue _searchQueue;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public RequestLibQueueArtist(ISearchQueue searchQueue)
        {
            _searchQueue = searchQueue;
        }

        public override void Execute(IEvent @event)
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

        public override CommandPermissions GetPermissions() => CommandPermissions.AddTrack |
                                                               CommandPermissions.StartPlayback;
    }

    internal class RequestLibQueueAlbum : LimitedCommand
    {
        private readonly ISearchQueue _searchQueue;

        public RequestLibQueueAlbum(ISearchQueue searchQueue)
        {
            _searchQueue = searchQueue;
        }

        public override void Execute(IEvent @event)
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

        public override CommandPermissions GetPermissions() => CommandPermissions.AddTrack |
                                                               CommandPermissions.StartPlayback;
    }

    internal class RequestLibGenreArtists : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ISearchApi _searchApi;

        public RequestLibGenreArtists(ITinyMessengerHub hub, ISearchApi searchApi)
        {
            _hub = hub;
            _searchApi = searchApi;
        }

        public void Execute(IEvent @event)
        {
            var result = _searchApi.LibraryGetGenreArtists(@event.DataToString());
            var message = new SocketMessage(Constants.LibraryGenreArtists, result);
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }

    internal class RequestLibArtistAlbums : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ISearchApi _searchApi;

        public RequestLibArtistAlbums(ITinyMessengerHub hub, ISearchApi searchApi)
        {
            _hub = hub;
            _searchApi = searchApi;
        }

        public void Execute(IEvent @event)
        {
            var result = _searchApi.LibraryGetArtistAlbums(@event.DataToString());
            var message = new SocketMessage(Constants.LibraryArtistAlbums, result);
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }

    internal class RequestLibAlbumTracks : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ISearchApi _searchApi;

        public RequestLibAlbumTracks(ITinyMessengerHub hub, ISearchApi searchApi)
        {
            _hub = hub;
            _searchApi = searchApi;
        }

        public void Execute(IEvent @event)
        {
            var results = _searchApi.LibraryGetAlbumTracks(@event.DataToString());
            var message = new SocketMessage(Constants.LibraryAlbumTracks, results);
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
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