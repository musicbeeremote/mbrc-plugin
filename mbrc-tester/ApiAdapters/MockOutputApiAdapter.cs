using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Model;

namespace MbrcTester.ApiAdapters
{
    public class MockOutputApiAdapter : IOutputApiAdapter
    {
        private string[] devices = new[]
        {
            "default",
            "sound1",
            "sound2",
            "sound3",
        };

        private string selectedDevice;

        public MockOutputApiAdapter()
        {
            selectedDevice = devices[0];
        }

        public bool SetOutputDevice(string outputDevice)
        {
            selectedDevice = outputDevice;
            return true;
        }

        public OutputDevice GetOutputDevices()
        {
            return new OutputDevice(devices, selectedDevice);
        }
    }
}