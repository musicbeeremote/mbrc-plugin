using AwesomeAssertions;
using MusicBeePlugin;
using MusicBeePlugin.Providers;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Providers
{
    /// <summary>
    /// Enabling scrobbling with no Last.fm account configured makes MusicBee pop
    /// a modal login dialog that blocks the plugin thread. <see
    /// cref="PlayerDataProvider.SetScrobble"/> must refuse to enable in that case
    /// (proxying "account configured" off a non-empty Last.fm user id) while
    /// still allowing it to be disabled.
    /// </summary>
    public class PlayerScrobbleGuardTests
    {
        private static Plugin.MusicBeeApiInterface ApiWith(
            string lastFmUserId,
            System.Action<bool> onSetEnabled)
        {
            var api = new Plugin.MusicBeeApiInterface
            {
                Setting_GetLastFmUserId = () => lastFmUserId,
                Player_SetScrobbleEnabled = enabled =>
                {
                    onSetEnabled(enabled);
                    return true;
                },
            };
            return api;
        }

        [Fact]
        public void SetScrobble_Enable_NoLastFmAccount_DoesNotCallApi()
        {
            var enableCalled = false;
            var api = ApiWith(string.Empty, _ => enableCalled = true);

            var result = new PlayerDataProvider(api).SetScrobble(true);

            result.Should().BeFalse("no account means we must not trigger the blocking login UI");
            enableCalled.Should().BeFalse();
        }

        [Fact]
        public void SetScrobble_Enable_WithLastFmAccount_CallsApi()
        {
            bool? enabledArg = null;
            var api = ApiWith("some-user", e => enabledArg = e);

            var result = new PlayerDataProvider(api).SetScrobble(true);

            result.Should().BeTrue();
            enabledArg.Should().Be(true);
        }

        [Fact]
        public void SetScrobble_Disable_IsAlwaysAllowed_EvenWithoutAccount()
        {
            bool? enabledArg = null;
            var api = ApiWith(string.Empty, e => enabledArg = e);

            var result = new PlayerDataProvider(api).SetScrobble(false);

            result.Should().BeTrue();
            enabledArg.Should().Be(false);
        }

        [Fact]
        public void SetScrobble_Enable_WhenUserIdDelegateUnbound_FallsBackToEnabling()
        {
            // Older MusicBee API revisions may not bind Setting_GetLastFmUserId;
            // when we can't tell, prefer enabling over blocking scrobbling.
            bool? enabledArg = null;
            var api = new Plugin.MusicBeeApiInterface
            {
                Setting_GetLastFmUserId = null,
                Player_SetScrobbleEnabled = e =>
                {
                    enabledArg = e;
                    return true;
                },
            };

            var result = new PlayerDataProvider(api).SetScrobble(true);

            result.Should().BeTrue();
            enabledArg.Should().Be(true);
        }
    }
}
