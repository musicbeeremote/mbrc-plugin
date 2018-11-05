using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Settings
{
    [DataContract]
    public class LimitedModeSettingsModel
    {
        
        /// <summary>
        /// If limited mode is enabled then the clients are only allowed to perform only the operations that 
        /// have been specifically enabled. 
        /// </summary>
        [DataMember(Name = "limited_mode")]
        public bool LimitedMode { get; set; }

        /// <summary>
        /// The maximum number of log entries that will be kept
        /// </summary>
        [DataMember(Name = "maximum_log_entries")]
        public uint MaxLogEntries { get; set; } = 3000;
    }
}