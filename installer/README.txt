MusicBee Remote Plugin
======================

INSTALLATION
------------

The contents of this archive must be extracted to the MusicBee plugins folder.
Usually this is: C:\Program Files (x86)\MusicBee\Plugins\

mb_remote.dll and mbrc_core.dll are both required: the plugin loads the native
core at startup, and they must sit side by side in the same folder.
The mbrc-helper.exe is optional and is used to add the Windows Firewall rule for
the listening port.

For Microsoft Store version of MusicBee:
Go to MusicBee -> Edit -> Preferences -> Plugins and use the "Add Plugin" button
to install directly from the zip file.


UNINSTALLATION
--------------

Delete mb_remote.dll from the Plugins folder.

The plugin stores its data under %APPDATA%\MusicBee\mb_remote\
This folder can be safely deleted after uninstallation.


REQUIREMENTS
------------

- MusicBee 3.1 or later
- Windows 7 or later


MORE INFORMATION
----------------

Website: https://mbrc.kelsos.net
GitHub:  https://github.com/musicbeeremote/mbrc-plugin
Help:    https://mbrc.kelsos.net/help/
