// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "This is part of the Plugin interface no need to do anything", Scope = "member", Target = "~M:MusicBeePlugin.Plugin.ReceiveNotification(System.String,MusicBeePlugin.Plugin.NotificationType)")]
[assembly: SuppressMessage("Build", "CA1707:Remote the underscores from assembly name mb_remote", Justification = "Plugin has to start with mb_", Scope = "module")]
[assembly: SuppressMessage("Build", "CA1801:Parameter reason of method Close is never used. Remove the parameter or use it in the method body.", Justification = "This is part of the Plugin interface no need to do anything", Scope = "member", Target = "~M:MusicBeePlugin.Plugin.Close(MusicBeePlugin.Plugin.PluginCloseReason)")]
[assembly: SuppressMessage("Build", "CA1801:Parameter panelHandle of method Configure is never used. Remove the parameter or use it in the method body.", Justification = "This is part of the Plugin interface no need to do anything", Scope = "member", Target = "~M:MusicBeePlugin.Plugin.Configure(System.IntPtr)~System.Boolean")]
[assembly: SuppressMessage("Build", "CA1801:Parameter sourceFileUrl of method ReceiveNotification is never used. Remove the parameter or use it in the method body.", Justification = "This is part of the Plugin interface no need to do anything", Scope = "member", Target = "~M:MusicBeePlugin.Plugin.ReceiveNotification(System.String,MusicBeePlugin.Plugin.NotificationType)")]
[assembly: SuppressMessage("Build", "CA1822:Member 'SaveSettings' does not access instance data and can be marked as static", Justification = "This is part of the Plugin interface no need to do anything", Scope = "member", Target = "~M:MusicBeePlugin.Plugin.SaveSettings")]
[assembly: SuppressMessage("Build", "CA1822:Member 'SaveSettings' does not access instance data and can be marked as static", Justification = "This is part of the Plugin interface no need to do anything", Scope = "member", Target = "~M:MusicBeePlugin.Plugin.SaveSettings")]
[assembly: SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Not handling specific exceptions", Scope = "member", Target = "~M:MusicBeePlugin.ApiAdapters.TrackApiAdapter.SetRating(System.String)~System.String")]
[assembly: SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "This is part of the Plugin interface no need to do anything", Scope = "member", Target = "~M:MusicBeePlugin.Plugin.Configure(System.IntPtr)~System.Boolean")]
[assembly: SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "This is part of the Plugin interface no need to do anything", Scope = "member", Target = "~M:MusicBeePlugin.Plugin.Close(MusicBeePlugin.Plugin.PluginCloseReason)")]
[assembly: SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "This is part of the Plugin interface no need to do anything", Scope = "member", Target = "~M:MusicBeePlugin.Plugin.ReceiveNotification(System.String,MusicBeePlugin.Plugin.NotificationType)")]
