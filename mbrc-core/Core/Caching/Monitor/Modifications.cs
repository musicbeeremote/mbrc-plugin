namespace MusicBeeRemote.Core.Caching.Monitor
{
    public class Modifications
    {
        public Modifications(string[] deletedFiles, string[] newFiles, string[] updatedFiles)
        {
            DeletedFiles = deletedFiles;
            NewFiles = newFiles;
            UpdatedFiles = updatedFiles;
        }

        public string[] DeletedFiles { get; }

        public string[] NewFiles { get; }

        public string[] UpdatedFiles { get; }
    }
}