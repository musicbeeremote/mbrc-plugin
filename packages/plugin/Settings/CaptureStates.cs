namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     The states <see cref="Ffi.CaptureStatus.state" /> can carry, mirroring
    ///     the <c>CAPTURE_*</c> constants in the core's
    ///     <c>diagnostics::capture</c>. Strings rather than a generated enum for
    ///     the same reason as <see cref="UpdateStates" />: the core owns the state
    ///     machine, and a value this side does not recognise must render as
    ///     "nothing is running" rather than fail to deserialize.
    /// </summary>
    internal static class CaptureStates
    {
        /// <summary>Nothing is being captured and nothing has been.</summary>
        public const string Idle = "idle";

        /// <summary>A capture is running; the core's log is at debug level.</summary>
        public const string Capturing = "capturing";

        /// <summary>The capture ended and the bundle is being written.</summary>
        public const string Writing = "writing";

        /// <summary>A bundle was written; <c>bundle_path</c> names it.</summary>
        public const string Done = "done";

        /// <summary>The safety auto-stop ended a capture nobody stopped.</summary>
        public const string Expired = "expired";

        /// <summary>The capture or the bundle failed; <c>message</c> says how.</summary>
        public const string Error = "error";
    }
}
