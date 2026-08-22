using AwesomeAssertions;
using MusicBeePlugin.Utilities;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Utilities
{
    /// <summary>
    ///     The pure half of <see cref="PackagePaths" />. The package-identity lookup
    ///     is a P/Invoke and is not exercised here; the mapping it feeds is, because
    ///     that is where a mistake would send Explorer somewhere wrong.
    /// </summary>
    public class PackagePathsTests
    {
        // The real values from the Store install this was found on.
        private const string Roaming = @"C:\Users\kelsos\AppData\Roaming";
        private const string Local = @"C:\Users\kelsos\AppData\Local";
        private const string Family = "50072StevenMayall.MusicBee_kcr266et74avj";

        [Fact]
        public void Translate_RewritesTheStoragePathIntoTheContainer()
        {
            var result = PackagePaths.Translate(
                @"C:\Users\kelsos\AppData\Roaming\MusicBee\mb_remote", Roaming, Local, Family);

            result.Should().Be(
                @"C:\Users\kelsos\AppData\Local\Packages\50072StevenMayall.MusicBee_kcr266et74avj\LocalCache\Roaming\MusicBee\mb_remote");
        }

        [Fact]
        public void Translate_RewritesAFileInsideTheStoragePath()
        {
            var result = PackagePaths.Translate(
                @"C:\Users\kelsos\AppData\Roaming\MusicBee\mb_remote\mbrc-core.log", Roaming, Local, Family);

            result.Should().EndWith(@"LocalCache\Roaming\MusicBee\mb_remote\mbrc-core.log");
        }

        [Fact]
        public void Translate_LeavesAPathOutsideRoamingAlone()
        {
            // A portable install keeps its storage beside the executable.
            const string portable = @"C:\dev\mbrc-plugin\app\MusicBee\AppData\mb_remote";
            PackagePaths.Translate(portable, Roaming, Local, Family).Should().Be(portable);

            // And the Desktop, which MSIX does not redirect.
            const string desktop = @"C:\Users\kelsos\Desktop\mbrc-diagnostics-20260822-115124.zip";
            PackagePaths.Translate(desktop, Roaming, Local, Family).Should().Be(desktop);
        }

        [Fact]
        public void Translate_IsIdempotent()
        {
            // Translating twice must not nest a second LocalCache\Roaming inside
            // the first - the panel can hand back a path it was already given.
            var once = PackagePaths.Translate(
                @"C:\Users\kelsos\AppData\Roaming\MusicBee\mb_remote", Roaming, Local, Family);
            PackagePaths.Translate(once, Roaming, Local, Family).Should().Be(once);
        }

        [Fact]
        public void Translate_OnlyMatchesAWholeSegment()
        {
            // "%APPDATA%" must not swallow a sibling directory that merely starts
            // with the same characters.
            const string sibling = @"C:\Users\kelsos\AppData\RoamingBackup\MusicBee";
            PackagePaths.Translate(sibling, Roaming, Local, Family).Should().Be(sibling);
        }

        [Fact]
        public void Translate_IsCaseInsensitiveOnTheRoot()
        {
            // MusicBee's own answer is not guaranteed to match the CLR's casing.
            var result = PackagePaths.Translate(
                @"c:\users\kelsos\appdata\roaming\MusicBee\mb_remote", Roaming, Local, Family);

            result.Should().EndWith(@"LocalCache\Roaming\MusicBee\mb_remote");
        }

        [Fact]
        public void Translate_HandlesTheRoamingRootItself()
        {
            PackagePaths.Translate(Roaming, Roaming, Local, Family)
                .Should().Be(@"C:\Users\kelsos\AppData\Local\Packages\" + Family + @"\LocalCache\Roaming");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Translate_PassesEmptyInputThrough(string path)
        {
            PackagePaths.Translate(path, Roaming, Local, Family).Should().Be(path);
        }

        [Fact]
        public void Translate_WithoutAFamilyNameChangesNothing()
        {
            // Not packaged: there is no container to point at.
            const string path = @"C:\Users\kelsos\AppData\Roaming\MusicBee\mb_remote";
            PackagePaths.Translate(path, Roaming, Local, string.Empty).Should().Be(path);
        }

        [Fact]
        public void ForExternalProcess_IsAPassThroughWhenNotPackaged()
        {
            // The test host is never a packaged app, so this exercises the guard.
            PackagePaths.IsPackaged.Should().BeFalse();

            const string path = @"C:\Users\kelsos\AppData\Roaming\MusicBee\mb_remote";
            PackagePaths.ForExternalProcess(path).Should().Be(path);
        }
    }
}
