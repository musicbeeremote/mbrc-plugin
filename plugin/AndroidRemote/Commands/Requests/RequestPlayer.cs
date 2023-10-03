using System;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestPlayer : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var client = Authenticator.Client(eEvent.ClientId);
            if (client != null)
            {
                var clientOs = eEvent.DataToString();
                if (clientOs.Equals("Android", StringComparison.InvariantCultureIgnoreCase))
                    client.ClientPlatform = ClientOS.Android;
                else if (clientOs.Equals("iOS", StringComparison.InvariantCultureIgnoreCase))
                    client.ClientPlatform = ClientOS.iOS;
                else
                    client.ClientPlatform = ClientOS.Unknown;
            }

            SocketServer.Instance.Send(new SocketMessage(Constants.Player, "MusicBee").ToJsonString(), eEvent.ClientId);
        }
    }
}