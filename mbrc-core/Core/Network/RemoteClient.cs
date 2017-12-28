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
        public RemoteClient()
        {
        }

        public RemoteClient(PhysicalAddress macAddress, IPAddress ipAddress)
        {
            MacAdress = macAddress;
            IpAddress = ipAddress;
            LastLogIn = DateTime.Now;
        }

        #region Properties

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
        public PhysicalAddress MacAdress { get; set; }

        [DisplayName("IP")]
        [DataMember(Name = "ip_address")]
        public IPAddress IpAddress { get; set; }

        [DisplayName("Last login")]
        [DataMember(Name = "last_login")]
        public DateTime LastLogIn { get; set; }

        #endregion Properties

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

        protected virtual void SetPermission(CommandPermissions permissions)
        {
            ClientPermissions |= permissions;
        }

        protected virtual void RemovePermission(CommandPermissions permissions)
        {
            ClientPermissions &= ~permissions;
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

        #region Equatable

        bool IEquatable<RemoteClient>.Equals(RemoteClient other)
        {
            return Equals(other);
        }

        public bool Equals(RemoteClient obj)
        {
            return MacAdress.ToString() == obj.MacAdress.ToString();
        }

        #endregion Equatable

        public override string ToString()
        {
            return $"{nameof(ClientPermissions)}: {ClientPermissions}," +
                   $" {nameof(ClientId)}: {ClientId}," +
                   $" {nameof(ActiveConnections)}: {ActiveConnections}," +
                   $" {nameof(MacAdress)}: {MacAdress}," +
                   $" {nameof(IpAddress)}: {IpAddress}," +
                   $" {nameof(LastLogIn)}: {LastLogIn}";
        }       
    }
}