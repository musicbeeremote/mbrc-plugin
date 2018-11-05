using MusicBeeRemote.Core.Model;

namespace MusicBeeRemote.Core.ApiAdapters
{
    public interface IOutputApiAdapter
    {
        /// <summary>
        /// Changes the default output device and returns the success of the operation.
        /// </summary>
        /// <param name="outputDevice">The new output device.</param>
        /// <returns>True for a successful operation and false for an non successful</returns>
        bool SetOutputDevice(string outputDevice);

        OutputDevice GetOutputDevices();
    }
}