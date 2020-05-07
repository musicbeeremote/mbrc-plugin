using System;
using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.Serialization;
using LiteDB;
using MusicBeeRemote.Core.Commands;

namespace MusicBeeRemote.Core.Network
{
    [DataContract(Name = "client")]
    public class RemoteClient : IEquatable<RemoteClient>
    {
        private readonly PhysicalAddress _macAddress;

        public RemoteClient(PhysicalAddress macAddress, IPAddress ipAddress)
        {
            _macAddress = macAddress;
            IpAddress = ipAddress;
            LastLogIn = DateTime.Now;
        }

        [DisplayName("Permissions")]
        [DataMember(Name = "permissions")]
        public CommandPermissions ClientPermissions { get; private set; } = CommandPermissions.None;

        [DisplayName("Client Id")]
        [BsonId]
        [DataMember(Name = "client_id")]
        public string ClientId { get; set; }

        [DisplayName("Connections")]
        [IgnoreDataMember]
        public uint ActiveConnections { get; private set; }

        [DisplayName("MAC")]
        [DataMember(Name = "mac_address")]
        public PhysicalAddress MacAddress => _macAddress;

        [DisplayName("IP")]
        [DataMember(Name = "ip_address")]
        public IPAddress IpAddress { get; set; }

        [DisplayName("Last login")]
        [DataMember(Name = "last_login")]
        public DateTime LastLogIn { get; set; }

        public virtual void AddConnection()
        {
            ActiveConnections++;
        }

        public virtual void RemoveConnection()
        {
            if (ActiveConnections > 0)
            {
                ActiveConnections--;
            }
        }

        public virtual void ResetConnection()
        {
            ActiveConnections = 0;
        }

        public virtual bool HasPermission(CommandPermissions permissions)
        {
            return ClientPermissions.HasFlag(permissions);
        }

        public virtual void SetPermission(CommandPermissions permissions, bool enable)
        {
            if (enable)
            {
                SetPermission(permissions);
            }
            else
            {
                RemovePermission(permissions);
            }
        }

        bool IEquatable<RemoteClient>.Equals(RemoteClient other)
        {
            return Equals(other);
        }

        public override bool Equals(object obj)
        {
            return ((IEquatable<RemoteClient>)this).Equals(obj as RemoteClient);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return MacAddress != null ? MacAddress.GetHashCode() : 0;
        }

        public override string ToString()
        {
            return $"{nameof(ClientPermissions)}: {ClientPermissions}," +
                   $" {nameof(ClientId)}: {ClientId}," +
                   $" {nameof(ActiveConnections)}: {ActiveConnections}," +
                   $" {nameof(MacAddress)}: {MacAddress}," +
                   $" {nameof(IpAddress)}: {IpAddress}," +
                   $" {nameof(LastLogIn)}: {LastLogIn}";
        }

        protected virtual void SetPermission(CommandPermissions permissions)
        {
            ClientPermissions |= permissions;
        }

        protected virtual void RemovePermission(CommandPermissions permissions)
        {
            ClientPermissions &= ~permissions;
        }

        private bool Equals(RemoteClient obj)
        {
            return MacAddress.ToString() == obj.MacAddress.ToString();
        }
    }
}
