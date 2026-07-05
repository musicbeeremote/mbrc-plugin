using System;
using System.Diagnostics;
using System.Windows.Forms;
using MusicBeePlugin.Adapters.Contracts;

namespace MusicBeePlugin.Adapters.Implementations
{
    /// <summary>
    ///     Implementation for MusicBee system operations.
    ///     Provides access to background task management and window handle.
    /// </summary>
    public class SystemOperations : ISystemOperations
    {
        private readonly Plugin.MusicBeeApiInterface _api;

        public SystemOperations(Plugin.MusicBeeApiInterface api)
        {
            _api = api;
        }

        /// <summary>
        ///     Creates a background task that runs on a separate thread.
        /// </summary>
        /// <param name="task">The action to execute in the background</param>
        public void CreateBackgroundTask(Action task)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            // Get the active form for the MusicBee API - falls back to null if not available
            var parentForm = Form.ActiveForm;
            _api.MB_CreateBackgroundTask(ThreadStart, parentForm);

            void ThreadStart()
            {
                try
                {
                    task();
                }
                catch (Exception ex)
                {
                    // Log to debug output to aid troubleshooting without crashing MusicBee
                    Debug.WriteLine($"[MusicBeeRemote] Background task failed: {ex.Message}");
                    Debug.WriteLine($"[MusicBeeRemote] Stack trace: {ex.StackTrace}");
                }
            }
        }

        /// <summary>
        ///     Sets the message displayed for the currently running background task.
        /// </summary>
        /// <param name="message">The progress message to display</param>
        public void SetBackgroundTaskMessage(string message)
        {
            _api.MB_SetBackgroundTaskMessage(message);
        }

        /// <summary>
        ///     Gets the handle to the main MusicBee window.
        /// </summary>
        /// <returns>Window handle</returns>
        public IntPtr GetWindowHandle()
        {
            return _api.MB_GetWindowHandle();
        }
    }
}
