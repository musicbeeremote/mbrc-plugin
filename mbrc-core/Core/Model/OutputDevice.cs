using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Model
{
    [DataContract]
    public class OutputDevice
    {
        public OutputDevice(IEnumerable<string> deviceNames, string activeDeviceName)
        {
            DeviceNames = new List<string>(deviceNames);
            ActiveDeviceName = activeDeviceName;
        }

        [DataMember(Name = "active")]
        public string ActiveDeviceName { get; }

        [DataMember(Name = "devices")]
        public List<string> DeviceNames { get; }
    }
}
