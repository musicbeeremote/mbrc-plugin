using System;
using System.Net;
using System.Net.NetworkInformation;
using MusicBeeRemote.Core.Commands;

namespace MusicBeeRemote.Core.Network
{
    [Serializable]
    public class RemoteClient : IEquatable<RemoteClient>
    {
        public RemoteClient(PhysicalAddress macAddress, IPAddress ipAddress)
        {
            _macAddress = macAddress;
            _ipAddress = ipAddress;
            LastLogIn = DateTime.Now;
        }

        #region vars

        private PhysicalAddress _macAddress;
        private DateTime _lastLogIn;
        private IPAddress _ipAddress;

        #endregion vars

        #region Properties

        public CommandPermissions ClientPermissions { get; private set; } = CommandPermissions.None;
        public string ClientId { get; set; }
        public uint ActiveConnections { get; private set; }

        public virtual void AddConnection()
        {
            ActiveConnections++;
        }

        public virtual void RemoveConnection()
        {
            ActiveConnections--;
        }

        public virtual bool HasPermission(CommandPermissions permissions)
        {
            return ClientPermissions.HasFlag(permissions);
        }

        public virtual void SetPermission(CommandPermissions permissions)
        {
            ClientPermissions |= permissions;
        }

        public virtual void RemovePermission(CommandPermissions permissions)
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

        public virtual PhysicalAddress MacAdress
        {
            get { return _macAddress; }
            set { _macAddress = value; }
        }

        public virtual IPAddress IpAddress
        {
            get { return _ipAddress; }
            set { _ipAddress = value; }
        }

        public DateTime LastLogIn
        {
            get { return _lastLogIn; }
            set { _lastLogIn = value; }
        }

        #endregion Properties

        #region Equatable

        bool IEquatable<RemoteClient>.Equals(RemoteClient other)
        {
            return Equals(other);
        }

        public bool Equals(RemoteClient obj)
        {
            var other = obj;

            return _macAddress.ToString() == other._macAddress.ToString();
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