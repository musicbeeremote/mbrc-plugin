using System.Collections.Generic;
using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLibQueueAlbum : ICommand
    {
        private readonly ILibraryService _libraryService;

        public RequestLibQueueAlbum(ILibraryService libraryService)
        {
            _libraryService = libraryService;
        }

        public void Execute(IEvent eEvent)
        {
            ((Dictionary<string, string>)eEvent.Data).TryGetValue("type", out var type);
            ((Dictionary<string, string>)eEvent.Data).TryGetValue("query", out var query);
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

            _libraryService.RequestQueueFiles(qType, MetaTag.Album, query);
        }
    }
}