using MusicBeePlugin.Enumerations;

namespace MusicBeePlugin.Utilities.Mapping
{
    internal static class QueueTypeMapper
    {
        public static QueueType MapFromString(string type)
        {
            switch (type)
            {
                case "next":
                    return QueueType.Next;
                case "last":
                    return QueueType.Last;
                case "now":
                    return QueueType.PlayNow;
                case "add-all":
                    return QueueType.AddAndPlay;
                default:
                    return QueueType.Next;
            }
        }
    }
}
