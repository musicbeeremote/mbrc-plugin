using System;
using MusicBeePlugin.Events;
using MusicBeePlugin.Events.Args;
using MusicBeePlugin.Model;

namespace MusicBeePlugin.Controller
{
    class PlayerStateController
    {
        private PlayerStateModel _playerStateModel;

        PlayerStateController()
        {
           EventDispatcher.Instance.PlayerStateEvent += HandlePlayerStateEvent;
           _playerStateModel = new PlayerStateModel();
        }

        private void HandlePlayerStateEvent(object sender, DataEventArgs eventArgs)
        {
            

        }
    }
}
