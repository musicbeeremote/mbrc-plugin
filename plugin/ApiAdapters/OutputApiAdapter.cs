using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Model;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.ApiAdapters
{
    /// <inheritdoc />
    public class OutputApiAdapter : IOutputApiAdapter
    {
        private readonly MusicBeeApiInterface _api;

        /// <summary>
        /// Initializes a new instance of the <see cref="OutputApiAdapter"/> class.
        /// </summary>
        /// <param name="api">The MusicBee API.</param>
        public OutputApiAdapter(MusicBeeApiInterface api)
        {
            _api = api;
        }

        /// <inheritdoc />
        public bool SetOutputDevice(string outputDevice)
        {
            return _api.Player_SetOutputDevice(outputDevice);
        }

        /// <inheritdoc />
        public OutputDevice GetOutputDevices()
        {
            _api.Player_GetOutputDevices(out var deviceNames, out var activeDeviceName);
            return new OutputDevice(deviceNames, activeDeviceName);
        }
    }
}
