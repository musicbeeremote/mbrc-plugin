MusicBee Remote Plugin
======================

INSTALLATION
------------

The contents of this archive must be extracted to the MusicBee plugins folder.
Usually this is: C:\Program Files (x86)\MusicBee\Plugins\

mb_remote.dll and mbrc_core.dll are both required: the plugin loads the native
core at startup, and they must sit side by side in the same folder.

mbrc-helper.exe is optional but recommended. It is the elevated helper, used for
two things: adding the Windows Firewall rule for the listening port, and
installing updates the plugin downloads. Without it, "Install and restart" in the
plugin's settings has nothing to run and updates have to be installed by hand.

For Microsoft Store version of MusicBee:
Go to MusicBee -> Edit -> Preferences -> Plugins and use the "Add Plugin" button
to install directly from the zip file.


UNINSTALLATION
--------------

Go to MusicBee -> Edit -> Preferences -> Plugins and use "Remove" on MusicBee
Remote. The plugin removes its own files - mbrc_core.dll, mbrc-helper.exe and
these two text files - along with everything it stored: the settings, the log,
the library and cover caches, and any downloaded update.

MusicBee deletes mb_remote.dll itself the next time it starts, so the Plugins
folder is only fully clear after a restart. Nothing else is left behind.

If you would rather do it by hand, close MusicBee and delete these from the
Plugins folder:

    mb_remote.dll
    mbrc_core.dll
    mbrc-helper.exe
    MBRC_LICENSE.txt
    MBRC_README.txt

then delete the data folder at %APPDATA%\MusicBee\mb_remote\


REQUIREMENTS
------------

- MusicBee 3.1 or later
- Windows 7 or later


MORE INFORMATION
----------------

Website: https://mbrc.kelsos.net
GitHub:  https://github.com/musicbeeremote/mbrc-plugin
Help:    https://mbrc.kelsos.net/help/
