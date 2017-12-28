using System.Collections.Generic;

namespace MusicBeeRemote.Core.Settings.Dialog.Whitelist
{
    public interface IWhitelistManagementView
    {
        void UpdateWhitelist(List<string> whitelist);
    }
}