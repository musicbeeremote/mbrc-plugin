namespace MusicBeeRemote.Core.Caching.Monitor
{
    public class Modifications
    {
        private readonly string[] _deletedFiles;
        private readonly string[] _newFiles;
        private readonly string[] _updatedFiles;

        public Modifications(string[] deletedFiles, string[] newFiles, string[] updatedFiles)
        {
            _deletedFiles = deletedFiles;
            _newFiles = newFiles;
            _updatedFiles = updatedFiles;
        }

        public string[] GetDeletedFiles()
        {
            return _deletedFiles;
        }

        public string[] GetNewFiles()
        {
            return _newFiles;
        }

        public string[] GetUpdatedFiles()
        {
            return _updatedFiles;
        }

        public override string ToString()
        {
            return
                $"Found {_newFiles.Length} new, {_deletedFiles.Length} deleted and {_updatedFiles.Length} updated files";
        }
    }
}
