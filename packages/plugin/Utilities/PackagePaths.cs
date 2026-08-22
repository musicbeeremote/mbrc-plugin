using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MusicBeePlugin.Utilities
{
    /// <summary>
    ///     Translates a path this process sees into one another process can open.
    ///
    ///     The Microsoft Store build of MusicBee is an MSIX package, and MSIX
    ///     redirects <c>%APPDATA%</c> transparently: the packaged process asks for
    ///     <c>C:\Users\x\AppData\Roaming\MusicBee\mb_remote</c> and the bytes land in
    ///     <c>%LOCALAPPDATA%\Packages\&lt;family&gt;\LocalCache\Roaming\MusicBee\mb_remote</c>.
    ///     Everything inside the container works, because the redirection is
    ///     invisible to it - the core logs the un-redirected path, reads and writes
    ///     it happily, and <see cref="Directory.Exists" /> agrees it is there.
    ///
    ///     It only breaks when the path leaves the container. Explorer runs
    ///     outside, resolves it literally, and reports "Location is not available"
    ///     for a folder that on that install does not exist at all - the real
    ///     <c>%APPDATA%\MusicBee</c> is never created.
    ///
    ///     That is also why an existence check cannot catch it: from in here the
    ///     path does exist. Detection has to be package identity.
    /// </summary>
    public static class PackagePaths
    {
        /// <summary>The process has no package identity (i.e. it is not packaged).</summary>
        private const int AppModelErrorNoPackage = 15700;

        private const int ErrorInsufficientBuffer = 122;

        /// <summary>Where MSIX puts a packaged app's redirected roaming AppData.</summary>
        private const string ContainerRoamingSuffix = @"LocalCache\Roaming";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetCurrentPackageFamilyName(ref uint length, StringBuilder name);

        private static readonly Lazy<string> Family = new Lazy<string>(ReadPackageFamilyName);

        /// <summary>
        ///     The package family name (e.g.
        ///     <c>50072StevenMayall.MusicBee_kcr266et74avj</c>), or empty when this
        ///     process is not packaged - which is the normal desktop install.
        /// </summary>
        public static string PackageFamilyName => Family.Value;

        /// <summary>Whether MusicBee is running as a packaged (Store) app.</summary>
        public static bool IsPackaged => !string.IsNullOrEmpty(Family.Value);

        /// <summary>
        ///     A path safe to hand to a process outside this one - Explorer, for
        ///     instance.
        ///
        ///     Unpackaged, or for a path that is not under roaming AppData (a
        ///     portable install keeps its storage beside the executable), the input
        ///     is returned untouched.
        ///
        ///     The translation is only used when the translated path can be seen to
        ///     exist. A wrong guess then costs nothing: the caller gets today's
        ///     behaviour rather than a second, differently wrong path.
        /// </summary>
        public static string ForExternalProcess(string path)
        {
            if (string.IsNullOrEmpty(path) || !IsPackaged) return path;

            try
            {
                var translated = Translate(
                    path,
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    PackageFamilyName);

                if (string.Equals(translated, path, StringComparison.OrdinalIgnoreCase)) return path;
                return Directory.Exists(translated) || File.Exists(translated) ? translated : path;
            }
            catch (Exception)
            {
                // Nothing here is worth failing an "open folder" button over.
                return path;
            }
        }

        /// <summary>
        ///     The pure half: rewrite <paramref name="path" /> from the roaming
        ///     AppData view into the package container's copy of it. Kept free of
        ///     any OS lookup so it can be tested directly.
        /// </summary>
        /// <returns>
        ///     The translated path, or <paramref name="path" /> unchanged when it is
        ///     not under <paramref name="roamingRoot" />.
        /// </returns>
        public static string Translate(string path, string roamingRoot, string localRoot, string family)
        {
            if (string.IsNullOrEmpty(path) ||
                string.IsNullOrEmpty(roamingRoot) ||
                string.IsNullOrEmpty(localRoot) ||
                string.IsNullOrEmpty(family))
            {
                return path;
            }

            // Already inside the container: translating again would nest a second
            // LocalCache\Roaming under the first.
            if (path.IndexOf(ContainerRoamingSuffix, StringComparison.OrdinalIgnoreCase) >= 0) return path;

            var root = roamingRoot.TrimEnd('\\');
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return path;

            // Only a whole path segment counts: %APPDATA% must not match
            // "...\RoamingSomethingElse".
            var remainder = path.Substring(root.Length);
            if (remainder.Length > 0 && remainder[0] != '\\') return path;
            remainder = remainder.TrimStart('\\');

            var container = Path.Combine(localRoot.TrimEnd('\\'), "Packages", family, ContainerRoamingSuffix);
            return remainder.Length == 0 ? container : Path.Combine(container, remainder);
        }

        /// <summary>
        ///     Asks Windows for this process's package family name.
        ///     <c>APPMODEL_ERROR_NO_PACKAGE</c> is the ordinary answer on a normal
        ///     desktop install and means "not packaged", not "failed".
        /// </summary>
        private static string ReadPackageFamilyName()
        {
            try
            {
                uint length = 0;
                var probe = GetCurrentPackageFamilyName(ref length, null);
                if (probe == AppModelErrorNoPackage || length == 0) return string.Empty;
                if (probe != ErrorInsufficientBuffer && probe != 0) return string.Empty;

                var buffer = new StringBuilder((int)length);
                return GetCurrentPackageFamilyName(ref length, buffer) == 0 ? buffer.ToString() : string.Empty;
            }
            catch (Exception)
            {
                // The export is Windows 8+. Older hosts are simply never packaged.
                return string.Empty;
            }
        }
    }
}
