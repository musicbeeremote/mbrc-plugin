using AwesomeAssertions;
using MusicBeePlugin.Ffi;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Ffi
{
    /// <summary>
    ///     A tick is a millisecond here, so the numbers in the lines read directly.
    /// </summary>
    public class HostCallStatsTests
    {
        private const long Ms = 1;
        private const long TicksPerSecond = 1000;

        [Fact]
        public void Record_ReportsALongCallOnItsOwn()
        {
            var stats = new HostCallStats(TicksPerSecond);

            string slow, summary;
            stats.Record("LibraryTrackTags", 40 * Ms, 250 * Ms, 7 * Ms, 0, out slow, out summary);

            slow.Should().Be("host call: LibraryTrackTags took 250ms after waiting 40ms for the cursor, packed in 7ms");
            summary.Should().BeNull();
        }

        [Fact]
        public void Record_StaysQuietForAShortCall()
        {
            var stats = new HostCallStats(TicksPerSecond);

            string slow, summary;
            stats.Record("PlayerState", 0, 2 * Ms, 0, 0, out slow, out summary);

            slow.Should().BeNull();
        }

        [Fact]
        public void Record_SummarizesEachKindOnceAWindowHasPassed()
        {
            var stats = new HostCallStats(TicksPerSecond);
            string slow, summary;
            stats.Record("PlayerState", 0, 2 * Ms, 1 * Ms, 0, out slow, out summary);
            stats.Record("PlayerState", 10 * Ms, 4 * Ms, 3 * Ms, 30000 * Ms, out slow, out summary);
            summary.Should().BeNull("the window is not over yet");

            stats.Record("LibraryTrackPaths", 0, 600 * Ms, 40 * Ms, 60000 * Ms, out slow, out summary);

            summary.Should().StartWith("host calls over 60s: 606ms in MusicBee (1.0%)");
            summary.Should().Contain("; LibraryTrackPaths n=1 call avg 600.0 max 600 wait avg 0.0 max 0 pack avg 40.0 max 40");
            summary.Should().Contain("; PlayerState n=2 call avg 3.0 max 4 wait avg 5.0 max 10 pack avg 2.0 max 3");
            summary.IndexOf("LibraryTrackPaths", System.StringComparison.Ordinal).Should().BeLessThan(
                summary.IndexOf("PlayerState", System.StringComparison.Ordinal), "the kind longest in MusicBee comes first");
        }

        [Fact]
        public void Record_StartsAFreshWindowAfterASummary()
        {
            var stats = new HostCallStats(TicksPerSecond);
            string slow, summary;
            stats.Record("PlayerState", 0, 1 * Ms, 0, 0, out slow, out summary);
            stats.Record("PlayerState", 0, 1 * Ms, 0, 60000 * Ms, out slow, out summary);
            summary.Should().NotBeNull();

            stats.Record("CoverData", 0, 1 * Ms, 0, 120000 * Ms, out slow, out summary);

            summary.Should().Contain("CoverData n=1");
            summary.Should().NotContain("PlayerState");
        }
    }
}
