using System;
using System.Collections.Generic;
using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.AndroidRemote.Interfaces;
using NLog;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLibSearchGenre : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibrarySearchGenres(eEvent.Data.ToString(), eEvent.ConnectionId);
        }
    }

    internal class RequestLibSearchArtist : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibrarySearchArtist(eEvent.Data.ToString(), eEvent.ConnectionId);
        }
    }

    internal class RequestLibSearchAlbum : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibrarySearchAlbums(eEvent.Data.ToString(), eEvent.ConnectionId);
        }
    }

    internal class RequestLibQueueTrack : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            string type, query;

            ((Dictionary<string, string>) eEvent.Data).TryGetValue("type", out type);
            ((Dictionary<string, string>) eEvent.Data).TryGetValue("query", out query);
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
        public void Execute(IEvent eEvent)
        {
            string type, query;

            ((Dictionary<string, string>) eEvent.Data).TryGetValue("type", out type);
            ((Dictionary<string, string>) eEvent.Data).TryGetValue("query", out query);
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

        public void Execute(IEvent eEvent)
        {
            try
            {
                string type, query;

                ((Dictionary<string, string>) eEvent.Data).TryGetValue("type", out type);
                ((Dictionary<string, string>) eEvent.Data).TryGetValue("query", out query);

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
        public void Execute(IEvent eEvent)
        {
            string type, query;

            ((Dictionary<string, string>) eEvent.Data).TryGetValue("type", out type);
            ((Dictionary<string, string>) eEvent.Data).TryGetValue("query", out query);
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
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibraryGetGenreArtists(eEvent.DataToString(), eEvent.ConnectionId);
        }
    }

    internal class RequestLibArtistAlbums : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibraryGetArtistAlbums(eEvent.DataToString(), eEvent.ConnectionId);
        }
    }

    internal class RequestLibAlbumTracks : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibraryGetAlbumTracks(eEvent.DataToString(), eEvent.ConnectionId);
        }
    }

    internal class RequestLibSearchTitle : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibrarySearchTitle(eEvent.Data.ToString(), eEvent.ConnectionId);
        }
    }
}