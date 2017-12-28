MusicBee Remote (Plugin)
====================

[![Join the chat at https://gitter.im/musicbee-remote/Lobby](https://badges.gitter.im/musicbee-remote/Lobby.svg)](https://gitter.im/musicbee-remote/Lobby?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)



About
-------
This is a plugin for [MusicBee](http://getmusicbee.com/) that is required for [MusicBee Remote](https://github.com/kelsos/mbrc) android application to function. The plugin acts as a socket server (TCP) that listens for incoming connections. The plugin implements a JSON based protocol functionality that is translated to calls to the MusicBee API.

This project is a split from the main repository as the development branch is now a completely different project.
Since the legacy (v1.x) plugin will receive some updates I am working on some major refactoring in order to be able to properly support
any new features before migrating to the v2.0 code base.

As soon as I find a way to embed resources I will be migrating to Visual Studio 2017. 

Building
-------
To build the plugin you have to open it with Visual Studio 2015. 
After opening the project you will probably have to restore the required packages with NuGet.

Protocol
-------
All the protocol messages have the following format ``{"context":"command","type":"type","data":"data"}`` you can find all the available commands at the [Constants.cs](https://github.com/kelsos/mbrc-plugin/blob/development/AndroidRemote/Networking/Constants.cs) file. The type can be has one of the following values **req** for request, **rep** for reply and **msg** for message, but they are currently ignored. The data contains the data part of the Protocol. In requests it contains data send to the server for example during the volume change it contains the integer of the new volume e.g. `` {"context":"playervolume","type":"req","data":10}``. In the replies it may contain more complex data like Arrays of tracks etc.

Thirdparty dependencies
-------


*   [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)
    
    [MIT](https://github.com/JamesNK/Newtonsoft.Json/blob/master/LICENSE.md)

*   [NLog](https://github.com/NLog/NLog)

    [BSD](https://github.com/NLog/NLog/blob/master/LICENSE.txt)

*   [TinyMessenger](https://github.com/grumpydev/TinyMessenger)

    [MS-PL](https://github.com/grumpydev/TinyMessenger/blob/master/licence.txt)

*   [LiteDB](https://github.com/mbdavid/LiteDB)

    [MIT](https://github.com/mbdavid/LiteDB/blob/master/LICENSE)

*   [StructureMap](https://github.com/structuremap/structuremap/)

    [Apache 2.0](https://github.com/structuremap/structuremap/blob/master/LICENSE.TXT)

License
------

    MusicBee Remote (Plugin for MusicBee)
    Copyright (C) 2017  Konstantinos Paparas

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
