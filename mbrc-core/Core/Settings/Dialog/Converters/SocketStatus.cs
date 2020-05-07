using System.Drawing;

namespace MusicBeeRemote.Core.Settings.Dialog.Converters
{
    public class SocketStatus
    {
        public SocketStatus(bool serviceStatus)
        {
            Listening = serviceStatus;
        }

        public string TextLabel => Listening ? "Running" : "Stopped";

        public Color LabelColor => Listening ? Color.Green : Color.Red;

        private bool Listening { get; }
    }
}
