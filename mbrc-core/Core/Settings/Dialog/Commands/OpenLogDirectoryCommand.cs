using System.Diagnostics;
using System.IO;

namespace MusicBeeRemote.Core.Settings.Dialog.Commands
{
    public class OpenLogDirectoryCommand
    {
        private readonly IStorageLocationProvider _storageLocationProvider;

        public OpenLogDirectoryCommand(IStorageLocationProvider storageLocationProvider)
        {
            _storageLocationProvider = storageLocationProvider;
        }

        public void Execute()
        {
            // todo create a proper log directory
            var logDirectory = _storageLocationProvider.StorageLocation();

            if (Directory.Exists(logDirectory))
            {
                Process.Start(logDirectory);
            }
        }
    }
}
