using MusicBeeRemote.Core.Caching.Monitor;

namespace MusicBeeRemote.Core.Settings.Dialog.Commands
{
    public class RefreshLibraryCommand
    {
        private readonly ILibraryScanner _scanner;

        public RefreshLibraryCommand(ILibraryScanner scanner)
        {
            _scanner = scanner;
        }

        public void Execute()
        {
            _scanner.RefreshLibrary();
        }
    }
}
