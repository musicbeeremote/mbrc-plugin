using System;
using System.Collections.Generic;
using System.Linq;

namespace MusicBeePlugin.AndroidRemote.Model
{
    class SettingsModel
    {
        public SettingsModel()
        {
            IpAddressList = new List<string>();
        }
        #region properties
        
        public int ListeningPort { get; set; }

        public FilteringSelection FilterSelection { get; set; }

        public string BaseIp { get; set; }

        public int LastOctetMax { get; set; }

        public List<string> IpAddressList { get; set; }
        #endregion

        #region methods

        public string GetValues()
        {
            switch (FilterSelection)
            {
                case FilteringSelection.All:
                    return String.Empty;
                case FilteringSelection.Range:
                    return FlattenAllowedRange();
                case FilteringSelection.Specific:
                    return FlattenAllowedAddressList();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void SetValues(string values)
        {
            switch (FilterSelection)
            {
                case FilteringSelection.All:
                    break;
                case FilteringSelection.Range:
                    UnFlattenAllowedRange(values);
                    break;
                case FilteringSelection.Specific:
                    UnflattenAllowedAddressList(values);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public string FlattenAllowedRange()
        {
            if(!String.IsNullOrEmpty(BaseIp) && (LastOctetMax>1||LastOctetMax<255))
            {
                return BaseIp + "," + LastOctetMax;
            }
            return String.Empty;
        }

        public void UnFlattenAllowedRange(string range)
        {
            if (String.IsNullOrEmpty(range)) return;
            string[] splitRange = range.Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            BaseIp = splitRange[0];
            LastOctetMax = int.Parse(splitRange[1]);
        }

        public string FlattenAllowedAddressList()
        {
            if (IpAddressList == null)
            {
                IpAddressList = new List<string>();
            }
            return IpAddressList.Aggregate<string, string>(
                null,
                (current, s) => current + (s + ",")
            );
        }

        public void UpdateFilteringSelection(string selection)
        {
            switch (selection)
            {
                case "All":
                    FilterSelection = FilteringSelection.All;
                    break;
                case "Range":
                    FilterSelection = FilteringSelection.Range;
                    break;
                case "Specific":
                    FilterSelection = FilteringSelection.Specific;
                    break;
            }
        }

        public void UnflattenAllowedAddressList(string allowedAddresses)
        {
            IpAddressList = new List<string>(allowedAddresses.Trim().Split(",".ToCharArray(),
                StringSplitOptions.RemoveEmptyEntries
            ));
        }
        #endregion
    }
}
