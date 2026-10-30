namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     The values <see cref="Ffi.FirewallStatus" /> and <see cref="Ffi.FirewallFixResult" />
    ///     carry, mirroring <c>mbrc-helper</c>'s <c>RuleStatus</c> and the core's
    ///     <c>firewall::fix</c>. Strings rather than a generated enum, as with
    ///     <see cref="UpdateStates" />: a value this side does not know reads as
    ///     blocked or failed rather than failing to deserialize.
    /// </summary>
    internal static class FirewallStates
    {
        public const string RuleMissing = "missing";
        public const string RuleDisabled = "disabled";

        /// <summary>The rule blocks, or applies to outgoing traffic.</summary>
        public const string RuleBlocking = "blocking";

        public const string RuleOtherPort = "other_port";

        /// <summary>The rule opens the port, but not on the network profile active now.</summary>
        public const string RuleOtherProfile = "other_profile";

        public const string RuleOpen = "open";

        /// <summary>For <c>firewall</c> and <c>public_network</c>: <c>on</c>, <c>off</c> or <c>unknown</c>.</summary>
        public const string On = "on";

        public const string Off = "off";

        public const string FixDone = "done";

        /// <summary>The user declined the permission prompt.</summary>
        public const string FixDeclined = "declined";

        /// <summary>The helper ran without administrator rights.</summary>
        public const string FixNotElevated = "not_elevated";

        /// <summary>The helper was still running at the core's deadline; it may yet succeed.</summary>
        public const string FixPending = "pending";
    }
}
