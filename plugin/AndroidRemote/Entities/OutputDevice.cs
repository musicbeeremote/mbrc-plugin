using System;

namespace MusicBeePlugin.AndroidRemote.Entities
{
    public class OutputDevice
    {
        private string[] deviceNames;
        private string activeDeviceName;

        public OutputDevice(string[] deviceNames, string activeDeviceName)
        {
            this.deviceNames = deviceNames;
            this.activeDeviceName = activeDeviceName;
        }

        public string ActiveDeviceName
        {
            get { return activeDeviceName; }

            set { activeDeviceName = value; }
        }

        public string[] DeviceNames
        {
            get { return deviceNames; }
            set { deviceNames = value; }
        }


    }
}
