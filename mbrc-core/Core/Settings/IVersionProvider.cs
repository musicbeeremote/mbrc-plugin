namespace MusicBeeRemote.Core.Settings
{
    public interface IVersionProvider
    {
        /// <summary>
        /// Gets the current plugin version.
        /// </summary>
        /// <returns>The current plugin version</returns>
        string GetPluginVersion();
    }

    class VersionProvider : IVersionProvider
    {
        private readonly string _version;

        public VersionProvider(string version)
        {
            _version = version;
        }

        public string GetPluginVersion()
        {
            return _version;
        }
    }
}
