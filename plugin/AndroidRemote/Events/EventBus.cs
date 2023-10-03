using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Events
{
    internal static class EventBus
    {
        public static void FireEvent(IEvent e)
        {
            Controller.Controller.Instance.CommandExecute(e);
        }
    }
}