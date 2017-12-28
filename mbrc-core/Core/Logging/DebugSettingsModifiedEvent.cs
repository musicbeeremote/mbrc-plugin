using TinyMessenger;

namespace MusicBeeRemote.Core.Logging
{
    /// <summary>
    /// Event dispatched by the configuration dialog to the log manager in order to change the logging mode.   
    /// </summary>
    /// <seealso cref="TinyMessenger.ITinyMessage" />
    internal class DebugSettingsModifiedEvent: ITinyMessage
    {
        /// <summary>
        /// Gets a value indicating whether the debug logging is enabled.
        /// </summary>
        /// <value>
        ///   <c>true</c> if debug logging enabled; otherwise, <c>false</c>.
        /// </value>
        public bool DebugLogEnabled { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DebugSettingsModifiedEvent"/> class.
        /// </summary>
        /// <param name="debugLogEnabled">If set to <c>true</c> verbose debug logging will be enabled.</param>
        public DebugSettingsModifiedEvent(bool debugLogEnabled)
        {
            DebugLogEnabled = debugLogEnabled;            
        }

        /// <summary>
        /// The sender of the message, or null if not supported by the message implementation.
        /// </summary>
        public object Sender { get; } = null;
    }
}