# MusicBee Remote (Plugin)

[![Build status](https://ci.appveyor.com/api/projects/status/awvi78psj8gtay89/branch/master?svg=true)](https://ci.appveyor.com/project/kelsos/plugin/branch/master)
[![codecov](https://codecov.io/gh/musicbeeremote/plugin/branch/master/graph/badge.svg)](https://codecov.io/gh/musicbeeremote/plugin)
[![Discord](https://img.shields.io/discord/420977901215678474.svg?style=popout)](https://discordapp.com/invite/rceTb57)
[![Join the chat at https://gitter.im/musicbee-remote/Lobby](https://badges.gitter.im/musicbee-remote/Lobby.svg)](https://gitter.im/musicbee-remote/Lobby?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)


## About

This is a plugin for [MusicBee](http://getmusicbee.com/) that is required for [MusicBee Remote](https://github.com/musicbeeremote/android-app) android application to function. The plugin acts as a socket server (TCP) that listens for incoming connections. The plugin implements a JSON based protocol functionality that is translated to calls to the MusicBee API.

The plugin acts as a server to provide data to the Android application using TCP sockets to transfer JSON formatted
messages.

## Building

The project requires Visual Studio 2017.

## Protocol

All the protocol messages have the following format ``{"context":"command","data":"data"}``.
[Constants.cs](https://github.com/musicbeeremote/plugin/blob/master/mbrc-core/Core/Network/Constants.cs) 
contains the available protocol commands. 
 
You can find a first attempt to document the protocol [here](https://github.com/musicbeeremote/plugin/blob/master/PROTOCOL.md).
The protocol is currently not sufficiently documented though.
 
## Thirdparty dependencies

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

## License

    MusicBee Remote (Plugin for MusicBee)
    Copyright (C) 2018  Konstantinos Paparas

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
