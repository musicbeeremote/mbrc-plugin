using System.Diagnostics.CodeAnalysis;

// Assembly-wide code analysis suppressions.
//
// MusicBeeInterface.cs is suppressed via .editorconfig instead: it is an
// external API file that must not be modified.

// Suppress assembly name warning - mb_remote.dll is the required name for MusicBee plugins
[assembly: SuppressMessage("Microsoft.Naming", "CA1707:RemoveUnderscoresFromMemberName", Scope = "module", Justification = "mb_remote.dll is the required assembly name for MusicBee plugin compatibility")]
