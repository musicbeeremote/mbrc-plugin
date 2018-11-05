using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;

namespace MusicBeeRemote.Core.Settings.Dialog.Commands
{
    public class OpenLogDirectoryCommand
    {
        private readonly IStorageLocationProvider _storageLocationProvider;

        public OpenLogDirectoryCommand(IStorageLocationProvider storageLocationProvider)
        {
            _storageLocationProvider = storageLocationProvider;
        }

        public void Execute(object parameter)
        {
            //todo create a proper log directory
            var logDirectory = _storageLocationProvider.StorageLocation();

            if (Directory.Exists(logDirectory))
            {
                Process.Start(logDirectory);
            }
        }
    }
}