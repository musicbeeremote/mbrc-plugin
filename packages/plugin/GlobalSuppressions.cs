using System.Diagnostics.CodeAnalysis;

// This file is used to configure or suppress code analysis warnings
// for the entire assembly.
//
// Note: MusicBeeInterface.cs warnings are suppressed via .editorconfig
// since it's an external API file that should not be modified.

// Suppress assembly name warning - mb_remote.dll is the required name for MusicBee plugins
[assembly: SuppressMessage("Microsoft.Naming", "CA1707:RemoveUnderscoresFromMemberName", Scope = "module", Justification = "mb_remote.dll is the required assembly name for MusicBee plugin compatibility")]

// Suppress SHA1 usage warnings - SHA1 is only used for non-cryptographic hashing (caching/identification)
// Changing to SHA256 would break client compatibility that expects SHA1 hashes
[assembly: SuppressMessage("Microsoft.Security", "CA5350:DoNotUseWeakCryptographicAlgorithms", Scope = "member", Target = "~M:MusicBeePlugin.Utilities.Common.Utilities.Sha1Hash(System.Byte[])", Justification = "SHA1 used for non-cryptographic hashing, client compatibility required")]
[assembly: SuppressMessage("Microsoft.Security", "CA5350:DoNotUseWeakCryptographicAlgorithms", Scope = "member", Target = "~M:MusicBeePlugin.Utilities.Common.Utilities.Sha1Hash(System.IO.Stream)", Justification = "SHA1 used for non-cryptographic hashing, client compatibility required")]
