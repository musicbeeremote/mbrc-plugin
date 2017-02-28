namespace MusicBeePlugin.AndroidRemote.Events
{
    using Interfaces;

    internal class EventBus
    {
        public static void FireEvent(IEvent e)
        {
            Controller.Controller.Instance.CommandExecute(e);
        }
    }
}
