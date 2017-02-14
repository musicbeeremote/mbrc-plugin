using System;
using System.Linq;
using System.Collections.Generic;
using mbrcPartyMode.Model;
using mbrcPartyMode.ViewModel.Commands;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using static mbrcPartyMode.Model.PartyModeModel;

namespace mbrcPartyMode.ViewModel
{

    public class ServerMessageView : ServerMessage
    {
        public ServerMessageView(string client, string command, bool deny) : base(client, command, deny)
        {
            MessageCount = 0;
        }

        public ServerMessageView(int count, ServerMessage msg) : base(msg.Client, msg.Command, msg.Deny)
        {
            MessageCount = count;
        }
        public int MessageCount { get; set; }
    }

}