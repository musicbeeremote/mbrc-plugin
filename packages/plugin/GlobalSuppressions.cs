using System.Diagnostics.CodeAnalysis;

// This file is used to configure or suppress code analysis warnings
// for the entire assembly.
//
// Note: MusicBeeInterface.cs warnings are suppressed via .editorconfig
// since it's an external API file that should not be modified.

// Suppress assembly name warning - mb_remote.dll is the required name for MusicBee plugins
[assembly: SuppressMessage("Microsoft.Naming", "CA1707:RemoveUnderscoresFromMemberName", Scope = "module", Justification = "mb_remote.dll is the required assembly name for MusicBee plugin compatibility")]
