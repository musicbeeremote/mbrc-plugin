namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     The states <see cref="Ffi.UpdateStatus.state" /> can carry, mirroring
    ///     the <c>STATE_*</c> constants in the core's <c>updates::service</c>.
    ///     Strings rather than a generated enum: the core owns the state machine,
    ///     and a value this side does not recognise must render as "unknown"
    ///     rather than fail to deserialize.
    /// </summary>
    internal static class UpdateStates
    {
        /// <summary>No check has run yet and nothing is staged.</summary>
        public const string Unknown = "unknown";

        /// <summary>A check is in flight.</summary>
        public const string Checking = "checking";

        /// <summary>The published release is not newer than what is running.</summary>
        public const string UpToDate = "up_to_date";

        /// <summary>A newer release is available and not yet downloaded.</summary>
        public const string Available = "available";

        /// <summary>The available release is being downloaded and staged.</summary>
        public const string Downloading = "downloading";

        /// <summary>A verified bundle is staged and waiting for a restart.</summary>
        public const string Staged = "staged";

        /// <summary>A newer release exists but the user asked to skip this one.</summary>
        public const string Skipped = "skipped";

        /// <summary>Automatic checking is off.</summary>
        public const string Disabled = "disabled";

        /// <summary>The last check failed.</summary>
        public const string Error = "error";

        /// <summary>
        ///     The download of an available update failed. Separate from
        ///     <see cref="Error" />: the update is still known, so the offer is to
        ///     retry rather than to check again.
        /// </summary>
        public const string DownloadFailed = "download_failed";
    }
}
