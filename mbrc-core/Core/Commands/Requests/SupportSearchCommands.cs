using System;
using System.Collections.Generic;
using MusicBeeRemote.Core.Enumerations;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Support;
using Newtonsoft.Json.Linq;
using NLog;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests
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
            var token = @event.DataToken();

            List<Artist> result = null;
            if (token != null && token.Type == JTokenType.String)
            {
                var query = token.Value<string>();
                result = _searchApi.LibraryGetGenreArtists(query);
            }
            
            var message = new SocketMessage(Constants.LibraryGenreArtists, result ?? new List<Artist>());
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
            var token = @event.DataToken();
            List<Album> result = null;
            if (token != null && token.Type == JTokenType.String)
            {
                var query = token.Value<string>();
                result = _searchApi.LibraryGetArtistAlbums(query);
            }
                      
            var message = new SocketMessage(Constants.LibraryArtistAlbums, result ?? new List<Album>());
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
            var token = @event.DataToken();
            List<Track> results = null; 
            if (token != null && token.Type == JTokenType.String)
            {
                var query = token.Value<string>();
                results = _searchApi.LibraryGetAlbumTracks(query);
            }

            var message = new SocketMessage(Constants.LibraryAlbumTracks, results ?? new List<Track>());
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
            var token = @event.DataToken();
            List<Track> searchResults = null;
            if (token != null && token.Type == JTokenType.String)
            {
                var query = token.Value<string>();
                searchResults = _searchApi.LibrarySearchTitle(query);
            }
            

            var message = new SocketMessage(Constants.LibrarySearchTitle, searchResults ?? new List<Track>());
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }
    }
}