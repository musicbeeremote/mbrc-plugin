using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Model;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.ApiAdapters
{
    public class OutputApiAdapter : IOutputApiAdapter
    {
        private readonly MusicBeeApiInterface _api;

        public OutputApiAdapter(MusicBeeApiInterface api)
        {
            _api = api;
        }

        public bool SetOutputDevice(string outputDevice)
        {
            return _api.Player_SetOutputDevice(outputDevice);
        }

        public OutputDevice GetOutputDevices()
        {
            string[] deviceNames;
            string activeDeviceName;

            _api.Player_GetOutputDevices(out deviceNames, out activeDeviceName);

            return new OutputDevice(deviceNames, activeDeviceName);
        }
    }
}