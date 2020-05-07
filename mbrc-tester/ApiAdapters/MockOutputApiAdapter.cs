using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Model;

namespace MbrcTester.ApiAdapters
{
    public class MockOutputApiAdapter : IOutputApiAdapter
    {
        private readonly string[] _devices =
        {
            "default",
            "sound1",
            "sound2",
            "sound3",
        };

        private string _selectedDevice;

        public MockOutputApiAdapter()
        {
            _selectedDevice = _devices[0];
        }

        public bool SetOutputDevice(string outputDevice)
        {
            _selectedDevice = outputDevice;
            return true;
        }

        public OutputDevice GetOutputDevices()
        {
            return new OutputDevice(_devices, _selectedDevice);
        }
    }
}
