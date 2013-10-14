MusicBee Remote (Plugin)
====================

About
-------
This is a plugin for [MusicBee](http://getmusicbee.com/) that is required for [MusicBee Remote](https://github.com/kelsos/mbrc) android application to function. The plugin acts as a socket server (TCP) that listens for incoming connections. The plugin implements a JSON based protocol functionality that is translated to calls to the MusicBee API.

Building
-------
To build the plugin you have to open it with Visual Studio 2012. After opening the project you will probably have to restore the required packages with NuGet.

Protocol
-------
All the protocol messages have the following format ``{"context":"command","type":"type","data":"data"}`` you can find all the available commands at the [Constants.cs](https://github.com/kelsos/mbrc-plugin/blob/development/AndroidRemote/Networking/Constants.cs) file. The type can be has one of the following values **req** for request, **rep** for reply and **msg** for message, but they are currently ignored. The data contains the data part of the Protocol. In requests it contains data send to the server for example during the volume change it contains the integer of the new volume e.g. `` {"context":"playervolume","type":"req","data":10}``. In the replies it may contain more complex data like Arrays of tracks etc.

Credits
-------
The plugin uses ServiceStack.Text to parse the JSON messages.

*   [ServiceStack.Text](https://github.com/ServiceStack/ServiceStack.Text)
    
    [GNU Affero General Public License v3](http://www.gnu.org/licenses/agpl-3.0.html)

License
------


    MusicBee Remote (Plugin for MusicBee)
    Copyright (C) 2013  Konstantinos Paparas

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
