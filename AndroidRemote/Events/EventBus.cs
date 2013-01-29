namespace MusicBeePlugin.AndroidRemote.Events
{
    using Interfaces;

    class EventBus
    {
        public static void FireEvent(IEvent e)
        {
            Controller.Controller.Instance.CommandExecute(e);
        }
    }
}
