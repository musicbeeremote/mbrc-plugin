using System;
using System.Collections.Generic;
using MusicBeeRemoteCore.Remote.Enumerations;
using MusicBeeRemoteCore.Remote.Interfaces;
using NLog;

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
            Plugin.Instance.RequestQueueFiles(qType, MetaTag.title, query);
        }
    }

    internal class RequestLibQueueGenre : ICommand
    {
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
            Plugin.Instance.RequestQueueFiles(qType, MetaTag.genre, query);
        }
    }

    internal class RequestLibQueueArtist : ICommand
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

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

                Plugin.Instance.RequestQueueFiles(qType, MetaTag.artist, query);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to queue artist");
            }
        }
    }

    internal class RequestLibQueueAlbum : ICommand
    {
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
            Plugin.Instance.RequestQueueFiles(qType, MetaTag.album, query);
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
        public void Execute(IEvent @event)
        {
            Plugin.Instance.LibrarySearchTitle(@event.Data.ToString(), @event.ConnectionId);
        }
    }
}