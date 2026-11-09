using System;
using System.Windows.Forms;
using MusicBeePlugin.Providers;

namespace MusicBeePlugin.Providers
{
    /// <summary>
    ///     Implementation for MusicBee system operations.
    ///     Provides access to background task progress messaging.
    /// </summary>
    public class SystemOperations : ISystemOperations
    {
        private readonly Plugin.MusicBeeApiInterface _api;

        public SystemOperations(Plugin.MusicBeeApiInterface api)
        {
            _api = api;
        }

        /// <inheritdoc />
        public bool CloseMusicBee()
        {
            if (!(Control.FromHandle(_api.MB_GetWindowHandle()) is Form main)) return false;
            main.BeginInvoke(new Action(main.Close));
            return true;
        }

        /// <inheritdoc />
        public void SetBackgroundTaskMessage(string message)
        {
            _api.MB_SetBackgroundTaskMessage(message);
        }
    }
}
