namespace MusicBeePlugin.Events.Messages
{
    /// <summary>
    ///     Published when the user saves settings that require the embedded
    ///     server to restart (e.g. the listening port). The Plugin host
    ///     subscribes and toggles the Rust core's networking off and on so
    ///     `core_settings.json` is re-read.
    /// </summary>
    public sealed class CoreRestartRequestedEvent
    {
    }
}
